import { Link } from 'react-router-dom';

interface PageTitleProps {
  title: string;
  subtitle?: string;
}

export default function PageTitle({ title, subtitle }: PageTitleProps) {
  return (
    <div className="row">
      <div className="col-12">
        <div className="page-title-box">
          <div className="page-title-right">
            <ol className="breadcrumb m-0">
              <li className="breadcrumb-item">
                <Link to="/">Inicio</Link>
              </li>
              {subtitle && <li className="breadcrumb-item">{subtitle}</li>}
              <li className="breadcrumb-item active">{title}</li>
            </ol>
          </div>
          <h4 className="page-title">{title}</h4>
        </div>
      </div>
    </div>
  );
}
