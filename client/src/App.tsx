import { Suspense, lazy, type ReactNode } from 'react';
import { BrowserRouter, Routes, Route, useLocation } from 'react-router-dom';
import AppErrorBoundary from './components/common/AppErrorBoundary';
import Layout from './components/layout/Layout';
import ProtectedRoute from './components/common/ProtectedRoute';
import HomeRedirect from './components/common/HomeRedirect';
import AccessRoute from './components/common/AccessRoute';

const Login = lazy(() => import('./pages/auth/Login'));
const Register = lazy(() => import('./pages/auth/Register'));
const ConfirmPhone = lazy(() => import('./pages/auth/ConfirmPhone'));
const ConfirmEmail = lazy(() => import('./pages/auth/ConfirmEmail'));
const ForgotPassword = lazy(() => import('./pages/auth/ForgotPassword'));
const ResetPassword = lazy(() => import('./pages/auth/ResetPassword'));
const AcceptInvitation = lazy(() => import('./pages/auth/AcceptInvitation'));
const Dashboard = lazy(() => import('./pages/dashboard/Dashboard'));
const Profile = lazy(() => import('./pages/profile/Profile'));
const MyOrders = lazy(() => import('./pages/account/MyOrders'));
const ProductsList = lazy(() => import('./pages/products/ProductsList'));
const ProductForm = lazy(() => import('./pages/products/ProductForm'));
const CategoriesList = lazy(() => import('./pages/categories/CategoriesList'));
const ServicesList = lazy(() => import('./pages/services/ServicesList'));
const ServiceBoard = lazy(() => import('./pages/services/ServiceBoard'));
const InventoryManagement = lazy(() => import('./pages/inventory/InventoryManagement'));
const BranchesList = lazy(() => import('./pages/branches/BranchesList'));
const SuppliersList = lazy(() => import('./pages/suppliers/SuppliersList'));
const PurchaseOrdersList = lazy(() => import('./pages/purchases/PurchaseOrdersList'));
const CashManagement = lazy(() => import('./pages/cash/CashManagement'));
const AppointmentsList = lazy(() => import('./pages/appointments/AppointmentsList'));
const OperationalReports = lazy(() => import('./pages/reports/OperationalReports'));
const KitchenBoard = lazy(() => import('./pages/kitchen/KitchenBoard'));
const AuditLogs = lazy(() => import('./pages/audit/AuditLogs'));
const PlatformConsole = lazy(() => import('./pages/platform/PlatformConsole'));
const PlansPage = lazy(() => import('./pages/platform/PlansPage'));
const OrdersList = lazy(() => import('./pages/orders/OrdersList'));
const OrdersCart = lazy(() => import('./pages/orders/OrdersCart'));
const SellPage = lazy(() => import('./pages/sell/SellPage'));
const TablesPage = lazy(() => import('./pages/tables/TablesPage'));
const PublicMenuPage = lazy(() => import('./pages/public/PublicMenuPage'));
const SetupPage = lazy(() => import('./pages/setup/SetupPage'));
const CustomersList = lazy(() => import('./pages/customers/CustomersList'));
const CampaignsPage = lazy(() => import('./pages/campaigns/CampaignsPage'));
const EmployeesList = lazy(() => import('./pages/employees/EmployeesList'));
const AiAgentSettings = lazy(() => import('./pages/ai-agent/AiAgentSettings'));
const BusinessSettings = lazy(() => import('./pages/settings/BusinessSettings'));
const WhatsAppSettings = lazy(() => import('./pages/settings/WhatsAppSettings'));
const WhatsAppChat = lazy(() => import('./pages/whatsapp/WhatsAppChat'));

function RouteLoadingFallback() {
  return <div className="p-3 text-muted">Cargando…</div>;
}

/** Screens outside the menu (sign-in, public menu) also recover from an error by changing route. */
function RouteErrorBoundary({ children }: { children: ReactNode }) {
  const { pathname } = useLocation();
  return <AppErrorBoundary resetKey={pathname}>{children}</AppErrorBoundary>;
}

export default function App() {
  return (
    <BrowserRouter>
      <RouteErrorBoundary>
        <Suspense fallback={<RouteLoadingFallback />}>
          <Routes>
            {/* Public routes */}
            <Route path="/login" element={<Login />} />
            <Route path="/register" element={<Register />} />
            <Route path="/confirm-phone" element={<ConfirmPhone />} />
            <Route path="/confirm-email" element={<ConfirmEmail />} />
            <Route path="/forgot-password" element={<ForgotPassword />} />
            <Route path="/reset-password" element={<ResetPassword />} />
            {/* The link of an invitation to a team (from the email or shared by WhatsApp). */}
            <Route path="/aceptar-invitacion" element={<AcceptInvitation />} />
            {/* The public menu of a business, opened from its link or QR (no sign-in). */}
            <Route path="/m/:slug" element={<PublicMenuPage />} />

            {/* Protected routes */}
            <Route
              element={
                <ProtectedRoute>
                  <Layout />
                </ProtectedRoute>
              }
            >
              <Route
                path="/dashboard"
                element={(
                  <AccessRoute area="dashboard">
                    <Dashboard />
                  </AccessRoute>
                )}
              />
              <Route path="/profile" element={<Profile />} />
              <Route
                path="/mis-pedidos"
                element={(
                  <AccessRoute area="myOrders">
                    <MyOrders />
                  </AccessRoute>
                )}
              />
              <Route
                path="/products"
                element={(
                  <AccessRoute area="catalog">
                    <ProductsList />
                  </AccessRoute>
                )}
              />
              <Route
                path="/products/new"
                element={(
                  <AccessRoute area="catalog">
                    <ProductForm />
                  </AccessRoute>
                )}
              />
              <Route
                path="/products/:id/edit"
                element={(
                  <AccessRoute area="catalog">
                    <ProductForm />
                  </AccessRoute>
                )}
              />
              <Route
                path="/categories"
                element={(
                  <AccessRoute area="catalog">
                    <CategoriesList />
                  </AccessRoute>
                )}
              />
              <Route
                path="/services"
                element={(
                  <AccessRoute area="catalog">
                    <ServicesList />
                  </AccessRoute>
                )}
              />
              <Route
                path="/services/board"
                element={(
                  <AccessRoute area="serviceBoard">
                    <ServiceBoard />
                  </AccessRoute>
                )}
              />
              <Route
                path="/inventory"
                element={(
                  <AccessRoute area="inventory">
                    <InventoryManagement />
                  </AccessRoute>
                )}
              />
              <Route
                path="/branches"
                element={(
                  <AccessRoute area="businessSettings">
                    <BranchesList />
                  </AccessRoute>
                )}
              />
              <Route
                path="/suppliers"
                element={(
                  <AccessRoute area="inventory">
                    <SuppliersList />
                  </AccessRoute>
                )}
              />
              <Route
                path="/purchases"
                element={(
                  <AccessRoute area="inventory">
                    <PurchaseOrdersList />
                  </AccessRoute>
                )}
              />
              <Route
                path="/cash"
                element={(
                  <AccessRoute area="cash">
                    <CashManagement />
                  </AccessRoute>
                )}
              />
              <Route
                path="/appointments"
                element={(
                  <AccessRoute area="appointments">
                    <AppointmentsList />
                  </AccessRoute>
                )}
              />
              <Route
                path="/reports"
                element={(
                  <AccessRoute area="reports">
                    <OperationalReports />
                  </AccessRoute>
                )}
              />
              <Route
                path="/kitchen"
                element={(
                  <AccessRoute area="kitchen">
                    <KitchenBoard />
                  </AccessRoute>
                )}
              />
              <Route
                path="/audit"
                element={(
                  <AccessRoute area="audit">
                    <AuditLogs />
                  </AccessRoute>
                )}
              />
              <Route
                path="/platform"
                element={(
                  <AccessRoute area="platform">
                    <PlatformConsole />
                  </AccessRoute>
                )}
              />
              <Route
                path="/platform/plans"
                element={(
                  <AccessRoute area="platform">
                    <PlansPage />
                  </AccessRoute>
                )}
              />
              <Route
                path="/orders"
                element={(
                  <AccessRoute area="orders">
                    <OrdersList />
                  </AccessRoute>
                )}
              />
              <Route
                path="/orders/cart"
                element={(
                  <AccessRoute area="sell">
                    <OrdersCart />
                  </AccessRoute>
                )}
              />
              <Route
                path="/sell"
                element={(
                  <AccessRoute area="sell">
                    <SellPage />
                  </AccessRoute>
                )}
              />
              <Route
                path="/tables"
                element={(
                  <AccessRoute area="tables">
                    <TablesPage />
                  </AccessRoute>
                )}
              />
              <Route
                path="/setup"
                element={(
                  <AccessRoute area="businessSettings">
                    <SetupPage />
                  </AccessRoute>
                )}
              />
              <Route
                path="/customers"
                element={(
                  <AccessRoute area="customers">
                    <CustomersList />
                  </AccessRoute>
                )}
              />
              <Route
                path="/campaigns"
                element={(
                  <AccessRoute area="campaigns">
                    <CampaignsPage />
                  </AccessRoute>
                )}
              />
              <Route
                path="/employees"
                element={(
                  <AccessRoute area="employees">
                    <EmployeesList />
                  </AccessRoute>
                )}
              />
              <Route
                path="/ai-agent"
                element={(
                  <AccessRoute area="aiAgent">
                    <AiAgentSettings />
                  </AccessRoute>
                )}
              />
              <Route
                path="/settings/business"
                element={(
                  <AccessRoute area="businessSettings">
                    <BusinessSettings />
                  </AccessRoute>
                )}
              />
              <Route
                path="/settings/whatsapp"
                element={(
                  <AccessRoute area="whatsAppSettings">
                    <WhatsAppSettings />
                  </AccessRoute>
                )}
              />
              <Route
                path="/whatsapp/chat"
                element={(
                  <AccessRoute area="chat">
                    <WhatsAppChat />
                  </AccessRoute>
                )}
              />
            </Route>

            {/* Default redirect: the first page the user can open */}
            <Route path="*" element={<HomeRedirect />} />
          </Routes>
        </Suspense>
      </RouteErrorBoundary>
    </BrowserRouter>
  );
}
