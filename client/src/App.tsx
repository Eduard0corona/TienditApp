import { Suspense, lazy } from 'react';
import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import Layout from './components/layout/Layout';
import ProtectedRoute from './components/common/ProtectedRoute';

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
            <Route path="/products" element={<ProductsList />} />
            <Route path="/products/new" element={<ProductForm />} />
            <Route path="/products/:id/edit" element={<ProductForm />} />
            <Route path="/categories" element={<CategoriesList />} />
            <Route path="/services" element={<ServicesList />} />
            <Route path="/orders" element={<OrdersList />} />
            <Route path="/orders/cart" element={<OrdersCart />} />
            <Route path="/customers" element={<CustomersList />} />
            <Route path="/ai-agent" element={<AiAgentSettings />} />
            <Route path="/settings/business" element={<BusinessSettings />} />
            <Route path="/settings/whatsapp" element={<WhatsAppSettings />} />
            <Route path="/whatsapp/chat" element={<WhatsAppChat />} />
          </Route>

          {/* Default redirect */}
          <Route path="*" element={<Navigate to="/dashboard" replace />} />
        </Routes>
      </Suspense>
    </BrowserRouter>
  );
}
