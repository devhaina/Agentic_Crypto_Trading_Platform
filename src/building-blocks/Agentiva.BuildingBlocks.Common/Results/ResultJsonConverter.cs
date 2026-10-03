using System.Text.Json;
using System.Text.Json.Serialization;

namespace Agentiva.BuildingBlocks.Common.Results;

/// <summary>
/// Serialises and deserialises <see cref="Result"/> and <see cref="Result{TValue}"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Result"/> and <see cref="Result{TValue}"/> deliberately have no
/// public constructor: every instance is built through
/// <see cref="Result.Success"/> or <see cref="Result.Failure"/>, which is what
/// makes "successful but carrying an error" and "failed but carrying no error"
/// unrepresentable. System.Text.Json's default reflection-based converter
/// requires a public parameterless constructor, a single public parameterised
/// one, or a <see cref="JsonConstructorAttribute"/>-annotated one — none of
/// which <see cref="Result{TValue}"/> has, by design. Without this converter,
/// serialising a <c>Result&lt;T&gt;</c> succeeds (serialisation only reads
/// properties) but deserialising one always throws
/// <see cref="NotSupportedException"/>.
/// </para>
/// <para>
/// That asymmetry is exactly the shape of bug most likely to escape ordinary
/// testing: a unit test that calls a handler and inspects the returned
/// <c>Result</c> never serialises it at all, and a test of the idempotency
/// store in isolation typically hands it a hand-written JSON string rather
/// than a real serialised <c>Result&lt;T&gt;</c>. The failure only appears on
/// the actual replay path — a financial command's idempotency key being
/// reused — which is precisely the path this converter exists to keep working.
/// </para>
/// </remarks>
public sealed class ResultJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
        => typeToConvert == typeof(Result)
           || (typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(Result<>));

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        if (typeToConvert == typeof(Result))
        {
            return new NonGenericResultConverter();
        }

        var valueType = typeToConvert.GetGenericArguments()[0];
        var converterType = typeof(GenericResultConverter<>).MakeGenericType(valueType);
        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }

    /// <summary>Converter for the non-generic <see cref="Result"/>.</summary>
    private sealed class NonGenericResultConverter : JsonConverter<Result>
    {
        public override Result Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var dto = JsonSerializer.Deserialize<ResultDto>(ref reader, options)
                      ?? throw new JsonException("A Result payload deserialised to null.");

            return dto.IsSuccess
                ? Result.Success()
                : Result.Failure(dto.Error ?? throw new JsonException("A failed Result payload had no error."));
        }

        public override void Write(Utf8JsonWriter writer, Result value, JsonSerializerOptions options)
            => JsonSerializer.Serialize(writer, new ResultDto(value.IsSuccess, value.IsFailure ? value.Error : null), options);
    }

    /// <summary>Converter for the generic <see cref="Result{TValue}"/>.</summary>
    private sealed class GenericResultConverter<TValue> : JsonConverter<Result<TValue>>
    {
        public override Result<TValue> Read(
            ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var dto = JsonSerializer.Deserialize<ResultDto<TValue>>(ref reader, options)
                      ?? throw new JsonException("A Result payload deserialised to null.");

            if (dto.IsSuccess)
            {
                // A success payload with no value is only possible for a
                // reference-typed TValue that was genuinely null, which
                // Result<TValue>.Value does not otherwise allow — but the
                // static factory takes whatever was written, including null
                // for a nullable reference type.
                return Result.Success(dto.Value!);
            }

            return Result.Failure<TValue>(
                dto.Error ?? throw new JsonException("A failed Result payload had no error."));
        }

        public override void Write(Utf8JsonWriter writer, Result<TValue> value, JsonSerializerOptions options)
        {
            var dto = value.IsSuccess
                ? new ResultDto<TValue>(true, value.Value, null)
                : new ResultDto<TValue>(false, default, value.Error);

            JsonSerializer.Serialize(writer, dto, options);
        }
    }

    /// <summary>Wire shape for a non-generic <see cref="Result"/>.</summary>
    private sealed record ResultDto(bool IsSuccess, Error? Error);

    /// <summary>Wire shape for a <see cref="Result{TValue}"/>.</summary>
    private sealed record ResultDto<TValue>(bool IsSuccess, TValue? Value, Error? Error);
}
