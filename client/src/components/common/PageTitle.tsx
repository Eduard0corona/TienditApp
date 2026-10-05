import { Link, useLocation } from 'react-router-dom';
import SectionTabs from '../layout/SectionTabs';
import { findNavSection } from '../layout/navigation';

interface PageTitleProps {
  title: string;
  subtitle?: string;
}

export default function PageTitle({ title, subtitle }: PageTitleProps) {
  const { pathname } = useLocation();
  // A page of a menu section is found under it (Inicio › Inventario › Compras), whatever the page calls it.
  const crumb = findNavSection(pathname)?.label ?? subtitle;

  return (
    <>
      <div className="row">
        <div className="col-12">
          <div className="page-title-box">
            <div className="page-title-right">
              <ol className="breadcrumb m-0">
                <li className="breadcrumb-item">
                  <Link to="/">Inicio</Link>
                </li>
                {crumb && crumb !== title && <li className="breadcrumb-item">{crumb}</li>}
                <li className="breadcrumb-item active">{title}</li>
              </ol>
            </div>
            <h4 className="page-title">{title}</h4>
          </div>
        </div>
      </div>
      <SectionTabs />
    </>
  );
}
