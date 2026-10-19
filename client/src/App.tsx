import { Suspense, lazy, type ReactNode } from 'react';
import { BrowserRouter, Routes, Route, useLocation } from 'react-router-dom';
import AppErrorBoundary from './components/common/AppErrorBoundary';
import Layout from './components/layout/Layout';
import ProtectedRoute from './components/common/ProtectedRoute';
import HomeRedirect from './components/common/HomeRedirect';
import AccessRoute from './components/common/AccessRoute';

const Login = lazy(() => import('./pages/auth/Login'));
const Register = lazy(() => import('./pages/auth/Register'));
const CheckEmail = lazy(() => import('./pages/auth/CheckEmail'));
const ConfirmPhone = lazy(() => import('./pages/auth/ConfirmPhone'));
const ConfirmEmail = lazy(() => import('./pages/auth/ConfirmEmail'));
const ForgotPassword = lazy(() => import('./pages/auth/ForgotPassword'));
const ResetPassword = lazy(() => import('./pages/auth/ResetPassword'));
const AcceptInvitation = lazy(() => import('./pages/auth/AcceptInvitation'));
const Dashboard = lazy(() => import('./pages/dashboard/Dashboard'));
const Profile = lazy(() => import('./pages/profile/Profile'));
const MyOrders = lazy(() => import('./pages/account/MyOrders'));
const MyOrderPage = lazy(() => import('./pages/account/MyOrderPage'));
const OrderTrackingPage = lazy(() => import('./pages/public/OrderTrackingPage'));
const MyAppointments = lazy(() => import('./pages/account/MyAppointments'));
const AppointmentTrackingPage = lazy(() => import('./pages/public/AppointmentTrackingPage'));
const AccountPage = lazy(() => import('./pages/account/AccountPage'));
const CustomerLayout = lazy(() => import('./components/layout/CustomerLayout'));
const ExplorePage = lazy(() => import('./pages/explore/ExplorePage'));
const ProductsList = lazy(() => import('./pages/products/ProductsList'));
const ProductForm = lazy(() => import('./pages/products/ProductForm'));
const CategoriesList = lazy(() => import('./pages/categories/CategoriesList'));
const PlatformPrices = lazy(() => import('./pages/products/PlatformPrices'));
const ServicesList = lazy(() => import('./pages/services/ServicesList'));
const ServiceBoard = lazy(() => import('./pages/services/ServiceBoard'));
const InventoryManagement = lazy(() => import('./pages/inventory/InventoryManagement'));
const BranchesList = lazy(() => import('./pages/branches/BranchesList'));
const DeliveryZones = lazy(() => import('./pages/settings/DeliveryZones'));
const OnlinePayments = lazy(() => import('./pages/settings/OnlinePayments'));
const SuppliersList = lazy(() => import('./pages/suppliers/SuppliersList'));
const PurchaseOrdersList = lazy(() => import('./pages/purchases/PurchaseOrdersList'));
const CashManagement = lazy(() => import('./pages/cash/CashManagement'));
const AppointmentsList = lazy(() => import('./pages/appointments/AppointmentsList'));
const OperationalReports = lazy(() => import('./pages/reports/OperationalReports'));
const PlatformSettlements = lazy(() => import('./pages/reports/PlatformSettlements'));
const PlatformSettlementPage = lazy(() => import('./pages/reports/PlatformSettlementPage'));
const KitchenBoard = lazy(() => import('./pages/kitchen/KitchenBoard'));
const AuditLogs = lazy(() => import('./pages/audit/AuditLogs'));
const PlatformConsole = lazy(() => import('./pages/platform/PlatformConsole'));
const PlansPage = lazy(() => import('./pages/platform/PlansPage'));
const PlatformReviewsPage = lazy(() => import('./pages/platform/PlatformReviewsPage'));
const OrdersList = lazy(() => import('./pages/orders/OrdersList'));
const OrdersCart = lazy(() => import('./pages/orders/OrdersCart'));
const SellPage = lazy(() => import('./pages/sell/SellPage'));
const TablesPage = lazy(() => import('./pages/tables/TablesPage'));
const PublicMenuPage = lazy(() => import('./pages/public/PublicMenuPage'));
const PublicBookingPage = lazy(() => import('./pages/public/PublicBookingPage'));
const OnlinePaymentReturnPage = lazy(() => import('./pages/public/OnlinePaymentReturnPage'));
const SetupPage = lazy(() => import('./pages/setup/SetupPage'));
const CustomersList = lazy(() => import('./pages/customers/CustomersList'));
const ReceivablesPage = lazy(() => import('./pages/customers/ReceivablesPage'));
const CampaignsPage = lazy(() => import('./pages/campaigns/CampaignsPage'));
const BusinessReviewsPage = lazy(() => import('./pages/reviews/BusinessReviewsPage'));
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
            <Route path="/check-email" element={<CheckEmail />} />
            <Route path="/confirm-phone" element={<ConfirmPhone />} />
            <Route path="/confirm-email" element={<ConfirmEmail />} />
            <Route path="/forgot-password" element={<ForgotPassword />} />
            <Route path="/reset-password" element={<ResetPassword />} />
            {/* The link of an invitation to a team (from the email or shared by WhatsApp). */}
            <Route path="/aceptar-invitacion" element={<AcceptInvitation />} />
            {/* The public menu of a business, opened from its link or QR (no sign-in). */}
            <Route path="/m/:slug" element={<PublicMenuPage />} />
            {/* Its booking page (Fase 24D): a service, a day and a free time, without signing in. */}
            <Route path="/m/:slug/agendar" element={<PublicBookingPage />} />
            {/* Where Mercado Pago sends the customer back after paying an order online (Fase 24I). */}
            <Route path="/m/:slug/pago/:checkoutId" element={<OnlinePaymentReturnPage />} />

            {/* The customer's screens: Explorar (no sign-in), and the orders, appointments and account of a customer account. */}
            <Route element={<CustomerLayout />}>
              <Route path="/explorar" element={<ExplorePage />} />
              <Route path="/explorar/:categoria" element={<ExplorePage />} />
              {/* The link of an online order's receipt (/seguimiento#link): no sign-in, the link is in the fragment. */}
              <Route path="/seguimiento" element={<OrderTrackingPage />} />
              {/* The link of an appointment booked online (/cita#link, Fase 24D), the same way. */}
              <Route path="/cita" element={<AppointmentTrackingPage />} />
              <Route
                path="/mis-citas"
                element={(
                  <ProtectedRoute>
                    <AccessRoute area="myOrders">
                      <MyAppointments />
                    </AccessRoute>
                  </ProtectedRoute>
                )}
              />
              <Route
                path="/mis-pedidos"
                element={(
                  <ProtectedRoute>
                    <AccessRoute area="myOrders">
                      <MyOrders />
                    </AccessRoute>
                  </ProtectedRoute>
                )}
              />
              <Route
                path="/mis-pedidos/:id"
                element={(
                  <ProtectedRoute>
                    <AccessRoute area="myOrders">
                      <MyOrderPage />
                    </AccessRoute>
                  </ProtectedRoute>
                )}
              />
              <Route
                path="/cuenta"
                element={(
                  <ProtectedRoute>
                    <AccessRoute area="myOrders">
                      <AccountPage />
                    </AccessRoute>
                  </ProtectedRoute>
                )}
              />
            </Route>

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
                path="/platform-prices"
                element={(
                  <AccessRoute area="catalog">
                    <PlatformPrices />
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
                path="/settings/delivery-zones"
                element={(
                  <AccessRoute area="businessSettings">
                    <DeliveryZones />
                  </AccessRoute>
                )}
              />
              <Route
                path="/settings/online-payments"
                element={(
                  <AccessRoute area="businessSettings">
                    <OnlinePayments />
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
                path="/reports/settlements"
                element={(
                  <AccessRoute area="reports">
                    <PlatformSettlements />
                  </AccessRoute>
                )}
              />
              <Route
                path="/reports/settlements/:id"
                element={(
                  <AccessRoute area="reports">
                    <PlatformSettlementPage />
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
                path="/platform/reviews"
                element={(
                  <AccessRoute area="platform">
                    <PlatformReviewsPage />
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
                path="/customers/receivables"
                element={(
                  <AccessRoute area="receivables">
                    <ReceivablesPage />
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
                path="/reviews"
                element={(
                  <AccessRoute area="reviews">
                    <BusinessReviewsPage />
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

            {/* Default redirect: Explorar for visitors and customers, the first page of their job for a team */}
            <Route path="*" element={<HomeRedirect />} />
          </Routes>
        </Suspense>
      </RouteErrorBoundary>
    </BrowserRouter>
  );
}
