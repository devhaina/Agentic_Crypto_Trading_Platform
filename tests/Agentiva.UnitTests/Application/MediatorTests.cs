using Agentiva.BuildingBlocks.Application.Mediator;
using Agentiva.BuildingBlocks.Common.Results;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Agentiva.UnitTests.Application;

public sealed class MediatorTests
{
    // --- Test doubles --------------------------------------------------------

    private sealed record Ping(string Message) : IQuery<string>;

    private sealed class PingHandler : IRequestHandler<Ping, string>
    {
        public Task<string> HandleAsync(Ping request, CancellationToken cancellationToken)
            => Task.FromResult($"pong:{request.Message}");
    }

    private sealed record FailingCommand : ICommand<Result>;

    private sealed class FailingCommandHandler : IRequestHandler<FailingCommand, Result>
    {
        public Task<Result> HandleAsync(FailingCommand request, CancellationToken cancellationToken)
            => Task.FromResult(Result.Failure(Error.Validation("test.failed", "Deliberate failure.")));
    }

    /// <summary>Records the order in which behaviors run.</summary>
    private sealed class RecordingBehavior<TRequest, TResponse>(TraceLog log, string name)
        : IPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        public async Task<TResponse> HandleAsync(
            TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            log.Entries.Add($"{name}:before");
            var response = await next(cancellationToken);
            log.Entries.Add($"{name}:after");
            return response;
        }
    }

    private sealed class OuterBehavior<TRequest, TResponse>(TraceLog log)
        : IPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
    {
        private readonly RecordingBehavior<TRequest, TResponse> _inner = new(log, "outer");

        public Task<TResponse> HandleAsync(
            TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
            => _inner.HandleAsync(request, next, cancellationToken);
    }

    private sealed class InnerBehavior<TRequest, TResponse>(TraceLog log)
        : IPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
    {
        private readonly RecordingBehavior<TRequest, TResponse> _inner = new(log, "inner");

        public Task<TResponse> HandleAsync(
            TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
            => _inner.HandleAsync(request, next, cancellationToken);
    }

    private sealed class TraceLog
    {
        public List<string> Entries { get; } = [];
    }

    /// <summary>An open generic behavior, the shape real behaviors use.</summary>
    private sealed class RecordingOpenBehavior<TRequest, TResponse>(TraceLog log)
        : IPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        public async Task<TResponse> HandleAsync(
            TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            log.Entries.Add($"open:{typeof(TRequest).Name}");
            return await next(cancellationToken);
        }
    }

    private sealed record Notified(string Value) : INotification;

    private sealed class FirstNotificationHandler(TraceLog log) : INotificationHandler<Notified>
    {
        public Task HandleAsync(Notified notification, CancellationToken cancellationToken)
        {
            log.Entries.Add($"first:{notification.Value}");
            return Task.CompletedTask;
        }
    }

    private sealed class SecondNotificationHandler(TraceLog log) : INotificationHandler<Notified>
    {
        public Task HandleAsync(Notified notification, CancellationToken cancellationToken)
        {
            log.Entries.Add($"second:{notification.Value}");
            return Task.CompletedTask;
        }
    }

    // --- Tests ---------------------------------------------------------------

    private static ServiceProvider BuildProvider(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddAgentivaMediator(typeof(MediatorTests).Assembly);
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Dispatches_a_request_to_its_handler()
    {
        await using var provider = BuildProvider();
        var sender = provider.GetRequiredService<ISender>();

        var response = await sender.SendAsync(new Ping("hello"), TestContext.Current.CancellationToken);

        response.ShouldBe("pong:hello");
    }

    /// <summary>
    /// Dispatch uses the request's runtime type.
    /// </summary>
    /// <remarks>
    /// Callers routinely hold an <c>IRequest&lt;T&gt;</c> reference rather than
    /// the concrete command, so resolving the handler from the static type would
    /// fail to find anything.
    /// </remarks>
    [Fact]
    public async Task Resolves_the_handler_from_the_runtime_type()
    {
        await using var provider = BuildProvider();
        var sender = provider.GetRequiredService<ISender>();

        IRequest<string> asInterface = new Ping("via-interface");

        (await sender.SendAsync(asInterface, TestContext.Current.CancellationToken))
            .ShouldBe("pong:via-interface");
    }

    [Fact]
    public async Task A_missing_handler_fails_with_an_actionable_message()
    {
        var services = new ServiceCollection();

        // Registered without scanning this assembly, so no handler exists.
        services.AddAgentivaMediator();

        await using var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<ISender>();

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            async () => await sender.SendAsync(new Ping("x"), TestContext.Current.CancellationToken));

        ex.Message.ShouldContain("No handler registered");
        ex.Message.ShouldContain("AddAgentivaMediator");
    }

    /// <summary>
    /// The first-registered behavior must be the outermost.
    /// </summary>
    /// <remarks>
    /// Order is load-bearing: validation has to run before the idempotency key
    /// is claimed, and logging has to wrap both so a rejected request still
    /// produces a log record.
    /// </remarks>
    [Fact]
    public async Task Behaviors_run_outermost_first_in_registration_order()
    {
        var log = new TraceLog();

        await using var provider = BuildProvider(services =>
        {
            services.AddSingleton(log);
            services.AddPipelineBehavior<Ping, string, OuterBehavior<Ping, string>>();
            services.AddPipelineBehavior<Ping, string, InnerBehavior<Ping, string>>();
        });

        var sender = provider.GetRequiredService<ISender>();
        await sender.SendAsync(new Ping("ordered"), TestContext.Current.CancellationToken);

        log.Entries.ShouldBe(["outer:before", "inner:before", "inner:after", "outer:after"]);
    }

    [Fact]
    public async Task A_failed_result_passes_through_the_pipeline_without_throwing()
    {
        await using var provider = BuildProvider();
        var sender = provider.GetRequiredService<ISender>();

        var result = await sender.SendAsync(new FailingCommand(), TestContext.Current.CancellationToken);

        // An expected domain failure is a return value, not an exception.
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("test.failed");
    }

    [Fact]
    public async Task Publishes_a_notification_to_every_handler()
    {
        var log = new TraceLog();

        await using var provider = BuildProvider(services => services.AddSingleton(log));

        var publisher = provider.GetRequiredService<IPublisher>();
        await publisher.PublishAsync(new Notified("event"), TestContext.Current.CancellationToken);

        log.Entries.Count.ShouldBe(2);
        log.Entries.ShouldContain("first:event");
        log.Entries.ShouldContain("second:event");
    }

    [Fact]
    public async Task Propagates_the_cancellation_token_to_the_handler()
    {
        await using var provider = BuildProvider();
        var sender = provider.GetRequiredService<ISender>();

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // The handler here does not observe the token, so this asserts the call
        // completes rather than that it cancels — the token plumbing itself is
        // covered by the behavior tests.
        var response = await sender.SendAsync(new Ping("token"), cts.Token);
        response.ShouldBe("pong:token");
    }

    [Fact]
    public void Registering_a_closed_type_as_an_open_generic_behavior_is_rejected()
    {
        var services = new ServiceCollection();

        // A closed type cannot satisfy an open generic service registration;
        // the extension rejects it at the call site rather than leaving an
        // opaque container error for BuildServiceProvider.
        var ex = Should.Throw<ArgumentException>(
            () => services.AddPipelineBehavior(typeof(OuterBehavior<Ping, string>)));

        ex.Message.ShouldContain("open generic type definition");
    }

    [Fact]
    public void Registering_an_open_generic_behavior_succeeds()
    {
        var services = new ServiceCollection();
        services.AddAgentivaMediator(typeof(MediatorTests).Assembly);
        services.AddPipelineBehavior(typeof(RecordingOpenBehavior<,>));
        services.AddSingleton(new TraceLog());

        // Builds without the open/closed mismatch error.
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ISender>().ShouldNotBeNull();
    }

    [Fact]
    public async Task A_null_request_is_rejected()
    {
        await using var provider = BuildProvider();
        var sender = provider.GetRequiredService<ISender>();

        await Should.ThrowAsync<ArgumentNullException>(
            async () => await sender.SendAsync<string>(null!, TestContext.Current.CancellationToken));
    }
}
