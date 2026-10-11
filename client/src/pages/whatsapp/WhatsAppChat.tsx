import { useState, useEffect, useRef, useCallback, useId } from 'react';
import { Offcanvas } from 'react-bootstrap';
import { useNavigate, useSearchParams } from 'react-router-dom';
import PageTitle from '../../components/common/PageTitle';
import CustomerCardPanel from '../../components/customers/CustomerCardPanel';
import { chatApi } from '../../api/chatApi';
import { useAuth } from '../../hooks/useAuth';
import { useChatSignalR } from '../../hooks/useChatSignalR';
import { useTenant } from '../../hooks/useTenant';
import type { ConversationDto, ChatMessageDto } from '../../types';
import { getAccess } from '../../utils/access';
import { buildSellLink } from '../../utils/sellLink';
import './WhatsAppChat.css';
import { getApiErrorMessage } from '../../api/errors';
import { formatInBusinessZone } from '../../utils/dates';
import { asButton } from '../../utils/a11y';

/** Same Mexican number whichever way it was stored (+52 or +521 and ten digits). */
function isSamePhone(a: string, b: string): boolean {
  const last10 = (value: string) => value.replace(/\D/g, '').slice(-10);
  return last10(a).length === 10 && last10(a) === last10(b);
}

function appendUnique(messages: ChatMessageDto[], message: ChatMessageDto): ChatMessageDto[] {
  return messages.some((existing) => existing.id === message.id) ? messages : [...messages, message];
}

/** The ticks of WhatsApp: one when sent, two when delivered, blue when read, a warning when it failed. */
function MessageTicks({ status }: { status: string }) {
  switch (status) {
    case 'Read':
      return <i className="ri-check-double-line ms-1 text-info" title="Leído" aria-label="Leído"></i>;
    case 'Delivered':
      return <i className="ri-check-double-line ms-1" title="Entregado" aria-label="Entregado"></i>;
    case 'Failed':
      return <i className="ri-error-warning-line ms-1 text-danger" title="No se entregó" aria-label="No se entregó"></i>;
    default:
      return <i className="ri-check-line ms-1" title="Enviado" aria-label="Enviado"></i>;
  }
}

export default function WhatsAppChat() {
  const titleId = useId();
  const [conversations, setConversations] = useState<ConversationDto[]>([]);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [messages, setMessages] = useState<ChatMessageDto[]>([]);
  const [loadingConvos, setLoadingConvos] = useState(true);
  const [loadingMsgs, setLoadingMsgs] = useState(false);
  const [search, setSearch] = useState('');
  const [newMessage, setNewMessage] = useState('');
  const [sending, setSending] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [showNewChat, setShowNewChat] = useState(false);
  const [newChatPhone, setNewChatPhone] = useState('');
  const [newChatMessage, setNewChatMessage] = useState('');
  const [startingChat, setStartingChat] = useState(false);
  const messagesEndRef = useRef<HTMLDivElement>(null);
  const { onNewMessage, onConversationUpdated, onMessageStatus } = useChatSignalR();
  const [now, setNow] = useState(() => Date.now());
  const [togglingReplies, setTogglingReplies] = useState(false);
  const { user } = useAuth();
  const { capabilities } = useTenant();
  const access = getAccess(user, capabilities);
  const navigate = useNavigate();
  const [searchParams, setSearchParams] = useSearchParams();
  const [showCard, setShowCard] = useState(false);

  const selectedConvo = conversations.find((c) => c.id === selectedId);
  // Latest selection, read by async callbacks that finish after the user moved to another conversation.
  const selectedIdRef = useRef<string | null>(selectedId);
  selectedIdRef.current = selectedId;

  // The pause of the automatic replies expires by itself: the header follows the clock.
  useEffect(() => {
    const clock = window.setInterval(() => setNow(Date.now()), 60_000);
    return () => window.clearInterval(clock);
  }, []);

  // Load conversations
  const loadConversations = useCallback(async () => {
    try {
      const { data } = await chatApi.getConversations(1, 100, search || undefined);
      setConversations(data.items);
      setError(null);
    } catch (err: unknown) {
      const errMsg = getApiErrorMessage(err, 'Error al cargar conversaciones');
      setError(errMsg);
    } finally {
      setLoadingConvos(false);
    }
  }, [search]);

  useEffect(() => {
    loadConversations();
  }, [loadConversations]);

  // Opened from a customer card or a sale (?conversation= or ?phone=): select that chat, or start one.
  useEffect(() => {
    if (loadingConvos) return;
    const conversationParam = searchParams.get('conversation');
    const phoneParam = searchParams.get('phone');
    if (!conversationParam && !phoneParam) return;
    const target = conversations.find((c) => c.id === conversationParam)
      ?? (phoneParam ? conversations.find((c) => isSamePhone(c.customerPhone, phoneParam)) : undefined);
    if (target) {
      setSelectedId(target.id);
    } else if (phoneParam) {
      setShowNewChat(true);
      setNewChatPhone(phoneParam);
    }
    setSearchParams({}, { replace: true });
  }, [conversations, loadingConvos, searchParams, setSearchParams]);

  // Load messages when selecting a conversation
  useEffect(() => {
    if (!selectedId) {
      setMessages([]);
      return;
    }
    // A slow response for a conversation the user already left must not replace the current one.
    let isStale = false;
    const load = async () => {
      setLoadingMsgs(true);
      try {
        const { data } = await chatApi.getMessages(selectedId, 1, 200);
        // API returns DESC, reverse to show oldest first
        if (!isStale) setMessages([...data.items].reverse());
      } catch {
        // silent
      } finally {
        if (!isStale) setLoadingMsgs(false);
      }
    };
    load();

    // Mark as read
    chatApi.markAsRead(selectedId).catch(() => {});
    setConversations((prev) =>
      prev.map((c) => (c.id === selectedId ? { ...c, unreadCount: 0 } : c))
    );

    return () => {
      isStale = true;
    };
  }, [selectedId]);

  // Auto-scroll to bottom
  useEffect(() => {
    messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, [messages]);

  // SignalR: new message
  const handleNewMessage = useCallback(
    (data: { conversation: ConversationDto; message: ChatMessageDto }) => {
      // Update conversation list
      setConversations((prev) => {
        const existing = prev.find((c) => c.id === data.conversation.id);
        if (existing) {
          return prev
            .map((c) => (c.id === data.conversation.id ? data.conversation : c))
            .sort((a, b) => new Date(b.lastMessageAt).getTime() - new Date(a.lastMessageAt).getTime());
        }
        return [data.conversation, ...prev];
      });

      // If this conversation is selected, append message (the hub also echoes messages sent from here)
      if (data.conversation.id === selectedId) {
        setMessages((prev) => appendUnique(prev, data.message));
        // Mark as read
        chatApi.markAsRead(data.conversation.id).catch(() => {});
        setConversations((prev) =>
          prev.map((c) => (c.id === data.conversation.id ? { ...c, unreadCount: 0 } : c))
        );
      }
    },
    [selectedId]
  );

  useEffect(() => {
    return onNewMessage(handleNewMessage);
  }, [onNewMessage, handleNewMessage]);

  // SignalR: conversation updated
  useEffect(() => {
    return onConversationUpdated((convo) => {
      setConversations((prev) =>
        prev.map((c) => (c.id === convo.id ? convo : c))
      );
    });
  }, [onConversationUpdated]);

  // SignalR: the ticks of a sent message move as WhatsApp reports them.
  useEffect(() => {
    return onMessageStatus((update) => {
      if (update.conversationId !== selectedIdRef.current) return;
      setMessages((prev) => prev.map((m) => (m.id === update.messageId ? { ...m, status: update.status } : m)));
    });
  }, [onMessageStatus]);

  const pausedUntil = selectedConvo?.autoRepliesPausedUntil ? new Date(selectedConvo.autoRepliesPausedUntil) : null;
  const repliesPaused = !!pausedUntil && pausedUntil.getTime() > now;

  const toggleAutoReplies = async () => {
    if (!selectedConvo || togglingReplies) return;
    setTogglingReplies(true);
    try {
      const { data } = await chatApi.setAutoReplies(selectedConvo.id, !repliesPaused);
      setConversations((prev) => prev.map((c) => (c.id === data.id ? { ...c, ...data } : c)));
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudo cambiar las respuestas automáticas.'));
    } finally {
      setTogglingReplies(false);
    }
  };

  // Start new conversation
  const handleStartChat = async () => {
    if (!newChatPhone.trim() || !newChatMessage.trim()) return;
    setStartingChat(true);
    try {
      const { data: sentMsg } = await chatApi.sendMessage({
        phoneNumber: newChatPhone.trim(),
        message: newChatMessage.trim(),
      });
      setShowNewChat(false);
      setNewChatPhone('');
      setNewChatMessage('');
      // Reload conversations and select the new one
      await loadConversations();
      setSelectedId(sentMsg.conversationId);
    } catch (err: unknown) {
      const errMsg = getApiErrorMessage(err, 'Error al iniciar conversación');
      setError(errMsg);
    } finally {
      setStartingChat(false);
    }
  };

  // Send message
  const handleSend = async () => {
    if (!newMessage.trim() || !selectedConvo || sending) return;
    setSending(true);
    try {
      const { data: sentMsg } = await chatApi.sendMessage({
        phoneNumber: selectedConvo.customerPhone,
        message: newMessage.trim(),
        conversationId: selectedConvo.id,
      });
      // Only if the user is still on that conversation; the hub echo is deduplicated by id.
      if (sentMsg.conversationId === selectedIdRef.current) {
        setMessages((prev) => appendUnique(prev, sentMsg));
      }
      setNewMessage('');
    } catch (err: unknown) {
      const errMsg = getApiErrorMessage(err, 'No se pudo enviar el mensaje.');
      setError(errMsg);
    } finally {
      setSending(false);
    }
  };

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      handleSend();
    }
  };

  const formatTime = (dateStr: string) => {
    const d = new Date(dateStr);
    const now = new Date();
    const isToday = d.toDateString() === now.toDateString();
    if (isToday) {
      return formatInBusinessZone(d, { hour: '2-digit', minute: '2-digit' });
    }
    return formatInBusinessZone(d, { day: '2-digit', month: 'short' });
  };

  const getInitials = (name?: string, phone?: string) => {
    if (name) {
      return name
        .split(' ')
        .slice(0, 2)
        .map((w) => w[0])
        .join('')
        .toUpperCase();
    }
    return phone?.slice(-2) || '??';
  };

  return (
    <>
      <PageTitle title="Chat" />

      {error && (
        <div className="alert alert-danger alert-dismissible fade show">
          {error}
          <button type="button" className="btn-close" onClick={() => setError(null)} />
        </div>
      )}

      {/* New conversation modal */}
      {showNewChat && (
        <div className="card mb-3">
          <div className="card-body">
            <h5 className="card-title mb-3">
              <i className="ri-chat-new-line me-1"></i>
              Nueva conversación
            </h5>
            <div className="row g-3">
              <div className="col-md-4">
                <input
                  type="text"
                  className="form-control"
                  placeholder="Teléfono a 10 dígitos"
                  value={newChatPhone}
                  onChange={(e) => setNewChatPhone(e.target.value)}
                />
              </div>
              <div className="col-md-6">
                <input
                  type="text"
                  className="form-control"
                  placeholder="Mensaje inicial..."
                  value={newChatMessage}
                  onChange={(e) => setNewChatMessage(e.target.value)}
                />
              </div>
              <div className="col-md-2 d-flex gap-2">
                <button
                  className="btn btn-success flex-grow-1"
                  onClick={handleStartChat}
                  disabled={startingChat || !newChatPhone.trim() || !newChatMessage.trim()}
                >
                  {startingChat ? <span className="spinner-border spinner-border-sm" /> : 'Enviar'}
                </button>
                <button type="button" className="btn btn-light" aria-label="Cerrar" onClick={() => setShowNewChat(false)}>
                  <i className="ri-close-line" aria-hidden="true"></i>
                </button>
              </div>
            </div>
          </div>
        </div>
      )}

      <div className="row">
        {/* Conversation List */}
        <div className="col-xl-4">
          <div className="card whatsapp-conversations-panel">
            <div className="card-body p-0 d-flex flex-column h-100">
              {/* Search + New */}
              <div className="p-3 border-bottom">
                <div className="d-flex gap-2">
                  <div className="input-group flex-grow-1">
                    <span className="input-group-text bg-transparent border-end-0">
                      <i className="ri-search-line"></i>
                    </span>
                    <input
                      type="text"
                      className="form-control border-start-0"
                      placeholder="Buscar..."
                      value={search}
                      onChange={(e) => setSearch(e.target.value)}
                    />
                  </div>
                  <button
                    className="btn btn-success btn-sm"
                    title="Nueva conversación"
                    onClick={() => setShowNewChat(!showNewChat)}
                  >
                    <i className="ri-add-line"></i>
                  </button>
                </div>
              </div>

              {/* List */}
              <div className="flex-grow-1 overflow-auto">
                {loadingConvos ? (
                  <div className="text-center py-5">
                    <div className="spinner-border spinner-border-sm text-primary" />
                  </div>
                ) : conversations.length === 0 ? (
                  <div className="text-center py-5 text-muted">
                    <i className="ri-chat-3-line fs-2 d-block mb-2"></i>
                    <p className="mb-0">No hay conversaciones</p>
                    <button
                      className="btn btn-sm btn-outline-success mt-2"
                      onClick={() => setShowNewChat(true)}
                    >
                      <i className="ri-add-line me-1"></i>
                      Iniciar chat
                    </button>
                  </div>
                ) : (
                  conversations.map((convo) => (
                    <div
                      key={convo.id}
                      className={`wa-conversation-item d-flex align-items-center p-3 border-bottom cursor-pointer ${
                        selectedId === convo.id ? 'bg-light' : ''
                      }`}
                      style={{ cursor: 'pointer' }}
                      aria-current={selectedId === convo.id ? 'true' : undefined}
                      {...asButton(() => setSelectedId(convo.id))}
                    >
                      {/* Avatar */}
                      <div className="flex-shrink-0 me-3">
                        <div
                          className="avatar-sm rounded-circle bg-success-subtle d-flex align-items-center justify-content-center"
                          style={{ width: 42, height: 42 }}
                        >
                          <span className="text-success fw-semibold">
                            {getInitials(convo.customerName, convo.customerPhone)}
                          </span>
                        </div>
                      </div>

                      {/* Content */}
                      <div className="flex-grow-1 overflow-hidden">
                        <h5 className="mb-0 fs-14 text-truncate">
                          {convo.customerName || convo.customerPhone}
                        </h5>
                        <p className="mb-0 text-muted text-truncate fs-13">
                          {convo.lastMessageBody || 'Sin mensajes'}
                        </p>
                      </div>

                      {/* Meta */}
                      <div className="flex-shrink-0 ms-2 text-end">
                        <small className="text-muted d-block">
                          {formatTime(convo.lastMessageAt)}
                        </small>
                        {convo.unreadCount > 0 && (
                          <span className="badge bg-success rounded-pill mt-1">
                            {convo.unreadCount}
                          </span>
                        )}
                      </div>
                    </div>
                  ))
                )}
              </div>
            </div>
          </div>
        </div>

        {/* Chat Panel */}
        <div className="col-xl-8">
          <div className="card whatsapp-messages-panel">
            {!selectedId ? (
              <div className="card-body d-flex align-items-center justify-content-center h-100">
                <div className="text-center text-muted">
                  <i className="ri-whatsapp-line fs-1 d-block mb-2 text-success"></i>
                  <h5>Chat de WhatsApp</h5>
                  <p className="mb-0">Selecciona una conversación para ver los mensajes</p>
                </div>
              </div>
            ) : (
              <div className="d-flex flex-column h-100">
                {/* Chat Header */}
                <div className="p-3 border-bottom d-flex align-items-center">
                  <div
                    className="avatar-sm rounded-circle bg-success-subtle d-flex align-items-center justify-content-center me-3"
                    style={{ width: 42, height: 42 }}
                  >
                    <span className="text-success fw-semibold">
                      {getInitials(selectedConvo?.customerName, selectedConvo?.customerPhone)}
                    </span>
                  </div>
                  <div className="flex-grow-1 overflow-hidden">
                    <h5 className="mb-0 text-truncate">
                      {selectedConvo?.customerName || selectedConvo?.customerPhone}
                    </h5>
                    {selectedConvo?.customerName && (
                      <small className="text-muted">{selectedConvo.customerPhone}</small>
                    )}
                    <small className={`d-block ${repliesPaused ? 'text-warning-emphasis' : 'text-muted'}`}>
                      {repliesPaused && pausedUntil
                        ? `Respuestas automáticas en pausa hasta las ${formatInBusinessZone(pausedUntil, { hour: '2-digit', minute: '2-digit' })}`
                        : 'Respuestas automáticas activas'}
                      <button
                        type="button"
                        className="btn btn-link btn-sm p-0 ms-2 align-baseline"
                        onClick={toggleAutoReplies}
                        disabled={togglingReplies}
                      >
                        {repliesPaused ? 'Reanudar' : 'Pausar'}
                      </button>
                    </small>
                  </div>
                  <div className="d-flex gap-2 flex-shrink-0">
                    {access.sell && selectedConvo && (
                      <button
                        type="button"
                        className="btn btn-sm btn-primary"
                        onClick={() => navigate(buildSellLink({
                          phone: selectedConvo.customerPhone,
                          name: selectedConvo.customerName,
                          conversationId: selectedConvo.id,
                        }))}
                      >
                        <i className="ri-shopping-basket-2-line me-1" aria-hidden="true"></i>
                        <span className="d-none d-sm-inline">Nuevo pedido</span>
                        <span className="visually-hidden d-sm-none">Nuevo pedido</span>
                      </button>
                    )}
                    <button type="button" className="btn btn-sm btn-light" onClick={() => setShowCard(true)}>
                      <i className="ri-profile-line me-1" aria-hidden="true"></i>
                      <span className="d-none d-sm-inline">Ficha</span>
                      <span className="visually-hidden d-sm-none">Ficha del cliente</span>
                    </button>
                  </div>
                </div>

                {/* Messages */}
                <div className="wa-messages flex-grow-1 overflow-auto p-3">
                  {loadingMsgs ? (
                    <div className="text-center py-5">
                      <div className="spinner-border spinner-border-sm text-primary" />
                    </div>
                  ) : messages.length === 0 ? (
                    <div className="text-center py-5">
                      <p className="wa-message-empty text-muted rounded p-2 d-inline-block">
                        No hay mensajes en esta conversación
                      </p>
                    </div>
                  ) : (
                    messages.map((msg) => {
                      const isOutbound = msg.direction === 'Outbound';
                      return (
                        <div
                          key={msg.id}
                          className={`d-flex mb-2 ${isOutbound ? 'justify-content-end' : 'justify-content-start'}`}
                        >
                          <div className={`wa-message-bubble ${isOutbound ? 'wa-message-out' : 'wa-message-in'} rounded p-2 position-relative`}>
                            <p className="mb-1" style={{ whiteSpace: 'pre-wrap', wordBreak: 'break-word' }}>
                              {msg.body}
                            </p>
                            <div className="text-end">
                              <small className="text-muted" style={{ fontSize: '0.7rem' }}>
                                {formatInBusinessZone(msg.sentAt, {
                                  hour: '2-digit',
                                  minute: '2-digit',
                                })}
                                {isOutbound && <MessageTicks status={msg.status} />}
                              </small>
                            </div>
                          </div>
                        </div>
                      );
                    })
                  )}
                  <div ref={messagesEndRef} />
                </div>

                {/* Input */}
                <div className="p-3 border-top">
                  <div className="d-flex gap-2">
                    <textarea
                      className="form-control"
                      rows={1}
                      placeholder="Escribe un mensaje..."
                      value={newMessage}
                      onChange={(e) => setNewMessage(e.target.value)}
                      onKeyDown={handleKeyDown}
                      disabled={sending}
                      style={{ resize: 'none' }}
                    />
                    <button
                      className="btn btn-success"
                      onClick={handleSend}
                      disabled={sending || !newMessage.trim()}
                    >
                      {sending ? (
                        <span className="spinner-border spinner-border-sm" />
                      ) : (
                        <i className="ri-send-plane-fill"></i>
                      )}
                    </button>
                  </div>
                </div>
              </div>
            )}
          </div>
        </div>
      </div>

      <Offcanvas show={showCard && !!selectedConvo} onHide={() => setShowCard(false)} placement="end" aria-labelledby={titleId}>
        <Offcanvas.Header closeButton>
          <Offcanvas.Title id={titleId} as="h2" className="h5">Ficha del cliente</Offcanvas.Title>
        </Offcanvas.Header>
        <Offcanvas.Body>
          {selectedConvo && (
            <CustomerCardPanel
              phone={selectedConvo.customerPhone}
              fallbackName={selectedConvo.customerName}
              inChat
              onNewOrder={access.sell
                ? (card) => navigate(buildSellLink({
                  phone: selectedConvo.customerPhone,
                  name: card.name ?? selectedConvo.customerName,
                  conversationId: selectedConvo.id,
                }))
                : undefined}
            />
          )}
        </Offcanvas.Body>
      </Offcanvas>
    </>
  );
}
