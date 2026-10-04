import type { UseFormRegisterReturn } from 'react-hook-form';
import { CfdiTaxObject } from '../../types';

interface SatFiscalFieldsProps {
  idPrefix: string;
  productCode: UseFormRegisterReturn;
  unitCode: UseFormRegisterReturn;
  taxObject: UseFormRegisterReturn;
  /** Code the unit gets when the product has none (H87, KGM...). */
  defaultUnitCode: string;
  errors: { productCode?: string; unitCode?: string };
}

/** CFDI 4.0 codes of a product: ClaveProdServ, ClaveUnidad and ObjetoImp. All optional. */
export default function SatFiscalFields({ idPrefix, productCode, unitCode, taxObject, defaultUnitCode, errors }: SatFiscalFieldsProps) {
  return (
    <details className="ta-sat-fields">
      <summary className="fw-semibold">Facturación (CFDI)</summary>
      <p className="form-text mt-1 mb-2">
        Opcional. Sin clave se factura con la genérica 01010101; tu contador puede darte las claves exactas del
        catálogo del SAT.
      </p>
      <div className="row g-2">
        <div className="col-md-4">
          <label className="form-label" htmlFor={`${idPrefix}-sat-product`}>Clave de producto o servicio</label>
          <input
            id={`${idPrefix}-sat-product`}
            className={`form-control ta-money ${errors.productCode ? 'is-invalid' : ''}`}
            inputMode="numeric"
            maxLength={10}
            placeholder="01010101"
            {...productCode}
          />
          {errors.productCode && <div className="invalid-feedback">{errors.productCode}</div>}
        </div>
        <div className="col-md-4">
          <label className="form-label" htmlFor={`${idPrefix}-sat-unit`}>Clave de unidad</label>
          <input
            id={`${idPrefix}-sat-unit`}
            className={`form-control text-uppercase ${errors.unitCode ? 'is-invalid' : ''}`}
            maxLength={5}
            placeholder={defaultUnitCode}
            {...unitCode}
          />
          {errors.unitCode
            ? <div className="invalid-feedback">{errors.unitCode}</div>
            : <div className="form-text">Vacío usa {defaultUnitCode}, la de su unidad.</div>}
        </div>
        <div className="col-md-4">
          <label className="form-label" htmlFor={`${idPrefix}-sat-object`}>Objeto de impuesto</label>
          <select id={`${idPrefix}-sat-object`} className="form-select" {...taxObject}>
            <option value={CfdiTaxObject.Subject}>02 · Sí objeto de impuesto</option>
            <option value={CfdiTaxObject.NotSubject}>01 · No objeto de impuesto</option>
          </select>
          <div className="form-text">Los exentos y de tasa 0 % también son 02.</div>
        </div>
      </div>
    </details>
  );
}
