import { useCallback, useEffect, useMemo, useState } from 'react';
import PageTitle from '../../components/common/PageTitle';
import TabRow from '../../components/common/TabRow';
import { getApiErrorMessage } from '../../api/errors';
import { inventoryApi } from '../../api/inventoryApi';
import type { InventoryAlertsDto, InventoryLocationDto, InventoryMovementDto, SupplyItemDto } from '../../types';
import AlertsTab from './AlertsTab';
import CountDetail from './CountDetail';
import CountsTab from './CountsTab';
import KardexTab from './KardexTab';
import LocationModal from './LocationModal';
import LocationsTab from './LocationsTab';
import MovementDetailModal from './MovementDetailModal';
import MovementModal from './MovementModal';
import MovementsTab from './MovementsTab';
import NewCountModal from './NewCountModal';
import RecipesTab from './RecipesTab';
import StocksTab from './StocksTab';
import SuppliesTab from './SuppliesTab';
import SupplyModal from './SupplyModal';
import { useKardexList, useMovementList, useStockList, useSupplyList } from './inventoryLists';
import { useRecipeEditor } from './useRecipeEditor';

type InventoryTab = 'supplies' | 'stocks' | 'movements' | 'counts' | 'locations' | 'recipes' | 'kardex' | 'alerts';

const TABS: Array<{ key: InventoryTab; label: string }> = [
  { key: 'supplies', label: 'Insumos' },
  { key: 'stocks', label: 'Existencias' },
  { key: 'movements', label: 'Entradas y salidas' },
  { key: 'counts', label: 'Conteos' },
  { key: 'kardex', label: 'Historial por insumo' },
  { key: 'recipes', label: 'Recetas' },
  { key: 'locations', label: 'Ubicaciones' },
  { key: 'alerts', label: 'Alertas' },
];

const TAB_KEYS = TABS.map((tab) => tab.key).join();

const EMPTY_ALERTS: InventoryAlertsDto = {
  lowStockCount: 0,
  criticalLowStockCount: 0,
  outOfStockCount: 0,
  missingRecipeProductCount: 0,
  items: [],
};

/** A dialog that opens with a fresh form each time: the new key mounts it again. */
function useDialog<T = never>() {
  const [state, setState] = useState<{ isOpen: boolean; target: T | null; key: number }>({
    isOpen: false,
    target: null,
    key: 0,
  });
  const show = useCallback((target: T | null = null) => {
    setState((current) => ({ isOpen: true, target, key: current.key + 1 }));
  }, []);
  const hide = useCallback(() => setState((current) => ({ ...current, isOpen: false })), []);
  return { ...state, show, hide };
}

/**
 * Supplies, stock, movements, physical counts, locations, recipes, kardex and alerts. The screen holds the state of every
 * tab, so filters, unsaved recipe changes and the count being counted stay while another tab is open; each tab and dialog
 * has its own file.
 */
export default function InventoryManagement() {
  const [activeTab, setActiveTab] = useState<InventoryTab>('supplies');
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');

  const supplies = useSupplyList(setError);
  const stocks = useStockList(setError);
  const movements = useMovementList(setError);
  const kardex = useKardexList(setError, activeTab === 'kardex');

  const [alerts, setAlerts] = useState<InventoryAlertsDto>(EMPTY_ALERTS);
  const [alertsLoading, setAlertsLoading] = useState(false);
  const [locations, setLocations] = useState<InventoryLocationDto[]>([]);
  const [locationsLoading, setLocationsLoading] = useState(true);
  const [supplyOptions, setSupplyOptions] = useState<SupplyItemDto[]>([]);

  const supplyDialog = useDialog<SupplyItemDto>();
  const locationDialog = useDialog<InventoryLocationDto>();
  const movementDialog = useDialog();
  const newCountDialog = useDialog();
  const [openCountId, setOpenCountId] = useState<string | null>(null);
  const [movementDetail, setMovementDetail] = useState<{
    show: boolean;
    loading: boolean;
    movement: InventoryMovementDto | null;
  }>({ show: false, loading: false, movement: null });

  const activeLocations = useMemo(
    () => locations.filter((location) => location.isActive),
    [locations],
  );

  const loadLocations = useCallback(async () => {
    setLocationsLoading(true);
    try {
      const { data } = await inventoryApi.getLocations();
      setLocations(data);
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'Error al cargar ubicaciones.'));
    } finally {
      setLocationsLoading(false);
    }
  }, []);

  const loadSupplyOptions = useCallback(async () => {
    try {
      const { data } = await inventoryApi.getSupplies({
        isActive: true,
        pageNumber: 1,
        pageSize: 500,
      });
      setSupplyOptions(data.items);
    } catch {
      setSupplyOptions([]);
    }
  }, []);

  const loadAlerts = useCallback(async () => {
    setAlertsLoading(true);
    try {
      const { data } = await inventoryApi.getAlerts();
      setAlerts(data);
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'Error al cargar alertas de inventario.'));
    } finally {
      setAlertsLoading(false);
    }
  }, []);

  const recipe = useRecipeEditor({
    setError,
    setSuccess,
    onSaved: () => Promise.all([loadAlerts(), kardex.reload()]),
  });

  useEffect(() => {
    loadLocations();
    loadSupplyOptions();
  }, [loadLocations, loadSupplyOptions]);

  useEffect(() => {
    if (activeTab !== 'alerts') return;
    loadAlerts();
  }, [activeTab, loadAlerts]);

  const toggleSupplyActive = async (supply: SupplyItemDto) => {
    setError('');
    setSuccess('');
    try {
      await inventoryApi.setSupplyActive(supply.id, !supply.isActive);
      setSuccess('Estado del insumo actualizado.');
      await Promise.all([supplies.reload(), loadSupplyOptions(), stocks.reload()]);
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudo actualizar el estado del insumo.'));
    }
  };

  const toggleLocationActive = async (location: InventoryLocationDto) => {
    setError('');
    setSuccess('');
    try {
      await inventoryApi.setLocationActive(location.id, !location.isActive);
      setSuccess('Estado de la ubicación actualizado.');
      await Promise.all([loadLocations(), stocks.reload()]);
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudo actualizar el estado de la ubicación.'));
    }
  };

  const openMovementDetail = async (movementId: string) => {
    setError('');
    setMovementDetail({ show: true, loading: true, movement: null });
    try {
      const { data } = await inventoryApi.getMovementById(movementId);
      setMovementDetail({ show: true, loading: false, movement: data });
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'Error al cargar el detalle del movimiento.'));
      setMovementDetail({ show: false, loading: false, movement: null });
    }
  };

  // A posted count moved the stock: the lists that show it are read again.
  const countPosted = (message: string) => {
    setError('');
    setSuccess(message);
    Promise.all([stocks.reload(), movements.reload(), supplies.reload(), kardex.reload(), loadSupplyOptions()]);
  };

  // After a dialog saves: its message, and the lists that show what it changed.
  const saved = (message: string, hide: () => void, reloads: Array<() => Promise<unknown>>) => {
    setError('');
    setSuccess(message);
    hide();
    Promise.all(reloads.map((reload) => reload()));
  };

  return (
    <>
      <PageTitle title="Inventario" />

      {error && (
        <div className="alert alert-danger alert-dismissible fade show">
          {error}
          <button type="button" className="btn-close" aria-label="Cerrar" onClick={() => setError('')}></button>
        </div>
      )}

      {success && (
        <div className="alert alert-success alert-dismissible fade show">
          {success}
          <button type="button" className="btn-close" aria-label="Cerrar" onClick={() => setSuccess('')}></button>
        </div>
      )}

      <div className="row">
        <div className="col-12">
          <div className="card">
            <div className="card-body">
              {/* The only row of tabs of Inventario, a menu section's row (it slides the same when they do not fit), with
                  the action of the open tab beside it. */}
              <div className="inventory-header">
                <TabRow role="tablist" label="Partes del inventario" contentKey={TAB_KEYS} activeKey={activeTab}>
                  {TABS.map((tab) => (
                    <button
                      key={tab.key}
                      type="button"
                      role="tab"
                      aria-selected={activeTab === tab.key}
                      className={`section-tab${activeTab === tab.key ? ' active' : ''}`}
                      onClick={() => setActiveTab(tab.key)}
                    >
                      {tab.label}
                    </button>
                  ))}
                </TabRow>

                {activeTab === 'supplies' && (
                  <button type="button" className="btn btn-primary btn-sm" onClick={() => supplyDialog.show()}>
                    <i className="ri-add-line me-1" aria-hidden="true"></i>Nuevo insumo
                  </button>
                )}
                {activeTab === 'movements' && (
                  <button type="button" className="btn btn-primary btn-sm" onClick={() => movementDialog.show()}>
                    <i className="ri-exchange-funds-line me-1" aria-hidden="true"></i>Registrar movimiento
                  </button>
                )}
                {activeTab === 'counts' && !openCountId && (
                  <button type="button" className="btn btn-primary btn-sm" onClick={() => newCountDialog.show()}>
                    <i className="ri-list-check-3 me-1" aria-hidden="true"></i>Nuevo conteo
                  </button>
                )}
                {activeTab === 'locations' && (
                  <button type="button" className="btn btn-primary btn-sm" onClick={() => locationDialog.show()}>
                    <i className="ri-add-line me-1" aria-hidden="true"></i>Nueva ubicación
                  </button>
                )}
                {activeTab === 'recipes' && (
                  <div className="d-flex align-items-center gap-2">
                    <button type="button" className="btn btn-light btn-sm" onClick={recipe.reload}>
                      <i className="ri-refresh-line me-1" aria-hidden="true"></i>Recargar
                    </button>
                    <button type="button" className="btn btn-primary btn-sm" onClick={recipe.save} disabled={recipe.saving || recipe.loading}>
                      <i className="ri-save-line me-1" aria-hidden="true"></i>{recipe.saving ? 'Guardando…' : 'Guardar receta'}
                    </button>
                  </div>
                )}
                {activeTab === 'kardex' && (
                  <button type="button" className="btn btn-light btn-sm" onClick={kardex.reload} disabled={kardex.loading}>
                    <i className="ri-refresh-line me-1" aria-hidden="true"></i>Actualizar
                  </button>
                )}
                {activeTab === 'alerts' && (
                  <button type="button" className="btn btn-light btn-sm" onClick={loadAlerts} disabled={alertsLoading}>
                    <i className="ri-refresh-line me-1" aria-hidden="true"></i>Actualizar
                  </button>
                )}
              </div>

              {activeTab === 'supplies' && (
                <SuppliesTab list={supplies} onEdit={supplyDialog.show} onToggleActive={toggleSupplyActive} />
              )}
              {activeTab === 'stocks' && <StocksTab list={stocks} locations={locations} />}
              {activeTab === 'movements' && <MovementsTab list={movements} onOpenDetail={openMovementDetail} />}
              {activeTab === 'counts' && !openCountId && <CountsTab locations={locations} onOpen={setOpenCountId} />}
              {activeTab === 'counts' && openCountId && (
                <CountDetail
                  key={openCountId}
                  countId={openCountId}
                  supplyOptions={supplyOptions}
                  onBack={() => setOpenCountId(null)}
                  onOpenMovement={openMovementDetail}
                  onPosted={countPosted}
                  onMessage={(message) => {
                    setError('');
                    setSuccess(message);
                  }}
                />
              )}
              {activeTab === 'recipes' && (
                <RecipesTab
                  editor={recipe}
                  supplyOptions={supplyOptions}
                  locations={locations}
                  activeLocations={activeLocations}
                />
              )}
              {activeTab === 'kardex' && <KardexTab list={kardex} supplyOptions={supplyOptions} locations={locations} />}
              {activeTab === 'alerts' && <AlertsTab alerts={alerts} loading={alertsLoading} />}
              {activeTab === 'locations' && (
                <LocationsTab
                  locations={locations}
                  loading={locationsLoading}
                  onEdit={locationDialog.show}
                  onToggleActive={toggleLocationActive}
                />
              )}
            </div>
          </div>
        </div>
      </div>

      <SupplyModal
        key={`supply-${supplyDialog.key}`}
        show={supplyDialog.isOpen}
        supply={supplyDialog.target}
        onHide={supplyDialog.hide}
        onSaved={(message) => saved(message, supplyDialog.hide, [supplies.reload, loadSupplyOptions, stocks.reload])}
      />

      <LocationModal
        key={`location-${locationDialog.key}`}
        show={locationDialog.isOpen}
        location={locationDialog.target}
        onHide={locationDialog.hide}
        onSaved={(message) => saved(message, locationDialog.hide, [loadLocations, stocks.reload, movements.reload])}
      />

      <MovementModal
        key={`movement-${movementDialog.key}`}
        show={movementDialog.isOpen}
        supplyOptions={supplyOptions}
        activeLocations={activeLocations}
        onHide={movementDialog.hide}
        onSaved={(message) => saved(
          message,
          movementDialog.hide,
          [movements.reload, stocks.reload, supplies.reload, loadSupplyOptions],
        )}
      />

      <NewCountModal
        key={`count-${newCountDialog.key}`}
        show={newCountDialog.isOpen}
        activeLocations={activeLocations}
        supplyOptions={supplyOptions}
        onHide={newCountDialog.hide}
        onStarted={(count) => {
          newCountDialog.hide();
          setError('');
          setSuccess(`Conteo ${count.countNumber} empezado: ${count.lineCount === 1 ? '1 artículo' : `${count.lineCount} artículos`} por contar.`);
          setOpenCountId(count.id);
        }}
      />

      <MovementDetailModal
        show={movementDetail.show}
        loading={movementDetail.loading}
        movement={movementDetail.movement}
        onHide={() => setMovementDetail((current) => ({ ...current, show: false }))}
      />
    </>
  );
}
