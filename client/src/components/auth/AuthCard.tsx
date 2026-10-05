import type { ReactNode } from 'react';
import { Link } from 'react-router-dom';
import BrandLogo from '../common/BrandLogo';

interface AuthCardProps {
  title: string;
  subtitle?: ReactNode;
  children: ReactNode;
  /** Below the card: usually the way back to the login. */
  footer?: ReactNode;
}

/** The frame of the screens before signing in: brand, title and one card. */
export default function AuthCard({ title, subtitle, children, footer }: AuthCardProps) {
  return (
    <div className="authentication-bg position-relative" style={{ minHeight: '100vh' }}>
      <div className="account-pages pt-2 pt-sm-5 pb-4 pb-sm-5 position-relative">
        <div className="container">
          <div className="row justify-content-center">
            <div className="col-xxl-4 col-lg-5">
              <div className="card">
                <div className="card-header py-4 text-center bg-primary">
                  <Link to="/">
                    <BrandLogo tone="light" />
                  </Link>
                </div>
                <div className="card-body p-4">
                  <div className="text-center w-75 m-auto">
                    <h1 className="h4 text-dark-50 text-center pb-0 fw-bold">{title}</h1>
                    {subtitle && <p className="text-muted mb-4">{subtitle}</p>}
                  </div>
                  {children}
                </div>
              </div>
              {footer && <div className="text-center mt-3">{footer}</div>}
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
