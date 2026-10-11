export default function Footer() {
  return (
    <footer className="ta-footer">
      <div className="container-fluid">
        <div className="row">
          <div className="col-md-6">
            {new Date().getFullYear()} &copy; TienditApp
          </div>
        </div>
      </div>
    </footer>
  );
}
