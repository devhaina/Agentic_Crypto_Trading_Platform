import { Routes } from '@angular/router';

/**
 * Dashboard routes.
 *
 * Every feature is lazily loaded. The dashboard is an operational tool whose
 * most important view — the one showing whether the platform is healthy and
 * what mode it is in — must render fast, and bundling the backtesting charts
 * into that first paint would work against it.
 */
export const routes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    redirectTo: 'dashboard',
  },
  {
    path: 'dashboard',
    title: 'Dashboard · Agentiva',
    loadComponent: () =>
      import('./features/dashboard/dashboard.component').then((m) => m.DashboardComponent),
  },
  {
    path: 'trading',
    title: 'Trading · Agentiva',
    loadComponent: () =>
      import('./features/trading/trading.component').then((m) => m.TradingComponent),
  },
  {
    path: 'orders',
    title: 'Orders · Agentiva',
    loadComponent: () => import('./features/orders/orders.component').then((m) => m.OrdersComponent),
  },
  {
    path: 'positions',
    title: 'Positions · Agentiva',
    loadComponent: () =>
      import('./features/positions/positions.component').then((m) => m.PositionsComponent),
  },
  {
    path: 'market',
    title: 'Market · Agentiva',
    loadComponent: () => import('./features/market/market.component').then((m) => m.MarketComponent),
  },
  {
    path: 'signals',
    title: 'Signals · Agentiva',
    loadComponent: () =>
      import('./features/signals/signals.component').then((m) => m.SignalsComponent),
  },
  {
    path: 'agents',
    title: 'AI Agents · Agentiva',
    loadComponent: () => import('./features/agents/agents.component').then((m) => m.AgentsComponent),
  },
  {
    path: 'strategies',
    title: 'Strategies · Agentiva',
    loadComponent: () =>
      import('./features/strategies/strategies.component').then((m) => m.StrategiesComponent),
  },
  {
    path: 'backtesting',
    title: 'Backtesting · Agentiva',
    loadComponent: () =>
      import('./features/backtesting/backtesting.component').then((m) => m.BacktestingComponent),
  },
  {
    path: 'risk',
    title: 'Risk · Agentiva',
    loadComponent: () => import('./features/risk/risk.component').then((m) => m.RiskComponent),
  },
  {
    path: 'audit',
    title: 'Audit · Agentiva',
    loadComponent: () => import('./features/audit/audit.component').then((m) => m.AuditComponent),
  },
  {
    path: 'settings',
    title: 'Settings · Agentiva',
    loadComponent: () =>
      import('./features/settings/settings.component').then((m) => m.SettingsComponent),
  },
  {
    path: 'administration',
    title: 'Administration · Agentiva',
    loadComponent: () =>
      import('./features/administration/administration.component').then(
        (m) => m.AdministrationComponent,
      ),
  },
  {
    path: '**',
    redirectTo: 'dashboard',
  },
];
