import { Suspense, lazy } from 'react';
import { BrowserRouter, Routes, Route } from 'react-router-dom';
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
const Dashboard = lazy(() => import('./pages/dashboard/Dashboard'));
const Profile = lazy(() => import('./pages/profile/Profile'));
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
const OrdersList = lazy(() => import('./pages/orders/OrdersList'));
const OrdersCart = lazy(() => import('./pages/orders/OrdersCart'));
const SellPage = lazy(() => import('./pages/sell/SellPage'));
const SetupPage = lazy(() => import('./pages/setup/SetupPage'));
const CustomersList = lazy(() => import('./pages/customers/CustomersList'));
const EmployeesList = lazy(() => import('./pages/employees/EmployeesList'));
const AiAgentSettings = lazy(() => import('./pages/ai-agent/AiAgentSettings'));
const BusinessSettings = lazy(() => import('./pages/settings/BusinessSettings'));
const WhatsAppSettings = lazy(() => import('./pages/settings/WhatsAppSettings'));
const WhatsAppChat = lazy(() => import('./pages/whatsapp/WhatsAppChat'));

function RouteLoadingFallback() {
  return <div className="p-3 text-muted">Cargando...</div>;
}

export default function App() {
  return (
    <BrowserRouter>
      <Suspense fallback={<RouteLoadingFallback />}>
        <Routes>
          {/* Public routes */}
          <Route path="/login" element={<Login />} />
          <Route path="/register" element={<Register />} />
          <Route path="/confirm-phone" element={<ConfirmPhone />} />
          <Route path="/confirm-email" element={<ConfirmEmail />} />
          <Route path="/forgot-password" element={<ForgotPassword />} />
          <Route path="/reset-password" element={<ResetPassword />} />

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
                <AccessRoute area="inventory">
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
    </BrowserRouter>
  );
}
