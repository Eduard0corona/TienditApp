import { Link } from 'react-router-dom';
import SectionTabs from '../layout/SectionTabs';

interface PageTitleProps {
  title: string;
  /**
   * The screen this one hangs from (Productos, for a product being edited). Only detail screens carry a trail:
   * on a screen of the menu it would just repeat the title.
   */
  parent?: { label: string; to: string };
}

export default function PageTitle({ title, parent }: PageTitleProps) {
  return (
    <>
      <div className="row">
        <div className="col-12">
          <div className="ta-page-title-box">
            {parent && (
              <nav className="ta-page-title-right" aria-label="Ruta de navegación">
                <ol className="breadcrumb m-0">
                  <li className="breadcrumb-item">
                    <Link to={parent.to}>{parent.label}</Link>
                  </li>
                  <li className="breadcrumb-item active" aria-current="page">{title}</li>
                </ol>
              </nav>
            )}
            <h4 className="ta-page-title">{title}</h4>
          </div>
        </div>
      </div>
      <SectionTabs />
    </>
  );
}
