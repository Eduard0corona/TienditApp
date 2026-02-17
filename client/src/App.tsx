import { Suspense, lazy } from 'react';
import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import Layout from './components/layout/Layout';
import ProtectedRoute from './components/common/ProtectedRoute';
import CapabilityRoute from './components/common/CapabilityRoute';
import { TenantCapability } from './types';
import { COMMERCE_CAPABILITIES } from './utils/tenantCapabilities';

const Login = lazy(() => import('./pages/auth/Login'));
const Register = lazy(() => import('./pages/auth/Register'));
const ConfirmPhone = lazy(() => import('./pages/auth/ConfirmPhone'));
const ForgotPassword = lazy(() => import('./pages/auth/ForgotPassword'));
const Dashboard = lazy(() => import('./pages/dashboard/Dashboard'));
const Profile = lazy(() => import('./pages/profile/Profile'));
const ProductsList = lazy(() => import('./pages/products/ProductsList'));
const ProductForm = lazy(() => import('./pages/products/ProductForm'));
const CategoriesList = lazy(() => import('./pages/categories/CategoriesList'));
const ServicesList = lazy(() => import('./pages/services/ServicesList'));
const InventoryManagement = lazy(() => import('./pages/inventory/InventoryManagement'));
const SuppliersList = lazy(() => import('./pages/suppliers/SuppliersList'));
const PurchaseOrdersList = lazy(() => import('./pages/purchases/PurchaseOrdersList'));
const CashManagement = lazy(() => import('./pages/cash/CashManagement'));
const AppointmentsList = lazy(() => import('./pages/appointments/AppointmentsList'));
const OperationalReports = lazy(() => import('./pages/reports/OperationalReports'));
const KitchenBoard = lazy(() => import('./pages/kitchen/KitchenBoard'));
const AuditLogs = lazy(() => import('./pages/audit/AuditLogs'));
const OrdersList = lazy(() => import('./pages/orders/OrdersList'));
const OrdersCart = lazy(() => import('./pages/orders/OrdersCart'));
const CustomersList = lazy(() => import('./pages/customers/CustomersList'));
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
          <Route path="/forgot-password" element={<ForgotPassword />} />

          {/* Protected routes */}
          <Route
            element={
              <ProtectedRoute>
                <Layout />
              </ProtectedRoute>
            }
          >
            <Route path="/dashboard" element={<Dashboard />} />
            <Route path="/profile" element={<Profile />} />
            <Route
              path="/products"
              element={(
                <CapabilityRoute required={COMMERCE_CAPABILITIES} mode="any">
                  <ProductsList />
                </CapabilityRoute>
              )}
            />
            <Route
              path="/products/new"
              element={(
                <CapabilityRoute required={COMMERCE_CAPABILITIES} mode="any">
                  <ProductForm />
                </CapabilityRoute>
              )}
            />
            <Route
              path="/products/:id/edit"
              element={(
                <CapabilityRoute required={COMMERCE_CAPABILITIES} mode="any">
                  <ProductForm />
                </CapabilityRoute>
              )}
            />
            <Route
              path="/categories"
              element={(
                <CapabilityRoute required={COMMERCE_CAPABILITIES} mode="any">
                  <CategoriesList />
                </CapabilityRoute>
              )}
            />
            <Route
              path="/services"
              element={(
                <CapabilityRoute required={COMMERCE_CAPABILITIES} mode="any">
                  <ServicesList />
                </CapabilityRoute>
              )}
            />
            <Route
              path="/inventory"
              element={(
                <CapabilityRoute required={TenantCapability.Inventory}>
                  <InventoryManagement />
                </CapabilityRoute>
              )}
            />
            <Route
              path="/suppliers"
              element={(
                <CapabilityRoute required={TenantCapability.Inventory}>
                  <SuppliersList />
                </CapabilityRoute>
              )}
            />
            <Route
              path="/purchases"
              element={(
                <CapabilityRoute required={TenantCapability.Inventory}>
                  <PurchaseOrdersList />
                </CapabilityRoute>
              )}
            />
            <Route
              path="/cash"
              element={(
                <CapabilityRoute required={COMMERCE_CAPABILITIES} mode="any">
                  <CashManagement />
                </CapabilityRoute>
              )}
            />
            <Route
              path="/appointments"
              element={(
                <CapabilityRoute required={TenantCapability.Appointments}>
                  <AppointmentsList />
                </CapabilityRoute>
              )}
            />
            <Route
              path="/reports"
              element={(
                <CapabilityRoute required={COMMERCE_CAPABILITIES} mode="any">
                  <OperationalReports />
                </CapabilityRoute>
              )}
            />
            <Route
              path="/kitchen"
              element={(
                <CapabilityRoute required={TenantCapability.Kitchen}>
                  <KitchenBoard />
                </CapabilityRoute>
              )}
            />
            <Route
              path="/audit"
              element={(
                <CapabilityRoute required={COMMERCE_CAPABILITIES} mode="any">
                  <AuditLogs />
                </CapabilityRoute>
              )}
            />
            <Route
              path="/orders"
              element={(
                <CapabilityRoute required={COMMERCE_CAPABILITIES} mode="any">
                  <OrdersList />
                </CapabilityRoute>
              )}
            />
            <Route
              path="/orders/cart"
              element={(
                <CapabilityRoute required={COMMERCE_CAPABILITIES} mode="any">
                  <OrdersCart />
                </CapabilityRoute>
              )}
            />
            <Route
              path="/customers"
              element={(
                <CapabilityRoute required={COMMERCE_CAPABILITIES} mode="any">
                  <CustomersList />
                </CapabilityRoute>
              )}
            />
            <Route
              path="/ai-agent"
              element={(
                <CapabilityRoute required={COMMERCE_CAPABILITIES} mode="any">
                  <AiAgentSettings />
                </CapabilityRoute>
              )}
            />
            <Route path="/settings/business" element={<BusinessSettings />} />
            <Route
              path="/settings/whatsapp"
              element={(
                <CapabilityRoute required={COMMERCE_CAPABILITIES} mode="any">
                  <WhatsAppSettings />
                </CapabilityRoute>
              )}
            />
            <Route
              path="/whatsapp/chat"
              element={(
                <CapabilityRoute required={COMMERCE_CAPABILITIES} mode="any">
                  <WhatsAppChat />
                </CapabilityRoute>
              )}
            />
          </Route>

          {/* Default redirect */}
          <Route path="*" element={<Navigate to="/dashboard" replace />} />
        </Routes>
      </Suspense>
    </BrowserRouter>
  );
}
