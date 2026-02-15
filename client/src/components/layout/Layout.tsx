import { Outlet } from 'react-router-dom';
import LeftSidebar from './LeftSidebar';
import Topbar from './Topbar';
import Footer from './Footer';

export default function Layout() {
  return (
    <div className="wrapper">
      <Topbar />
      <LeftSidebar />
      <div className="content-page">
        <div className="content">
          <div className="container-fluid">
            <Outlet />
          </div>
        </div>
        <Footer />
      </div>
    </div>
  );
}
