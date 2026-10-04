import { useContext } from 'react';
import { ConfirmContext } from '../components/feedback/feedbackContexts';

export function useConfirm() {
  const context = useContext(ConfirmContext);
  if (!context) throw new Error('useConfirm must be used within ConfirmProvider');
  return context;
}
