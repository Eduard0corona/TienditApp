import { useState, useEffect, useRef, useCallback } from 'react';
import PageTitle from '../../components/common/PageTitle';
import { chatApi } from '../../api/chatApi';
import { useChatSignalR } from '../../hooks/useChatSignalR';
import type { ConversationDto, ChatMessageDto } from '../../types';
import './WhatsAppChat.css';

export default function WhatsAppChat() {
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
  const { onNewMessage, onConversationUpdated } = useChatSignalR();

  const selectedConvo = conversations.find((c) => c.id === selectedId);

  // Load conversations
  const loadConversations = useCallback(async () => {
    try {
      const { data } = await chatApi.getConversations(1, 100, search || undefined);
      setConversations(data.items);
      setError(null);
    } catch (err: unknown) {
      const errMsg =
        (err as { response?: { data?: { error?: string } } })?.response?.data?.error ||
        (err as { message?: string })?.message ||
        'Error al cargar conversaciones';
      setError(errMsg);
    } finally {
      setLoadingConvos(false);
    }
  }, [search]);

  useEffect(() => {
    loadConversations();
  }, [loadConversations]);

  // Load messages when selecting a conversation
  useEffect(() => {
    if (!selectedId) {
      setMessages([]);
      return;
    }
    const load = async () => {
      setLoadingMsgs(true);
      try {
        const { data } = await chatApi.getMessages(selectedId, 1, 200);
        // API returns DESC, reverse to show oldest first
        setMessages([...data.items].reverse());
      } catch {
        // silent
      } finally {
        setLoadingMsgs(false);
      }
    };
    load();

    // Mark as read
    chatApi.markAsRead(selectedId).catch(() => {});
    setConversations((prev) =>
      prev.map((c) => (c.id === selectedId ? { ...c, unreadCount: 0 } : c))
    );
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

      // If this conversation is selected, append message
      if (data.conversation.id === selectedId) {
        setMessages((prev) => [...prev, data.message]);
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
      const errMsg =
        (err as { response?: { data?: { error?: string } } })?.response?.data?.error ||
        'Error al iniciar conversación';
      setError(errMsg);
    } finally {
      setStartingChat(false);
    }
  };

  // Send message
  const handleSend = async () => {
    if (!newMessage.trim() || !selectedConvo) return;
    setSending(true);
    try {
      const { data: sentMsg } = await chatApi.sendMessage({
        phoneNumber: selectedConvo.customerPhone,
        message: newMessage.trim(),
        conversationId: selectedConvo.id,
      });
      setMessages((prev) => [...prev, sentMsg]);
      setNewMessage('');
    } catch {
      // silent
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
      return d.toLocaleTimeString('es-MX', { hour: '2-digit', minute: '2-digit' });
    }
    return d.toLocaleDateString('es-MX', { day: '2-digit', month: 'short' });
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
      <PageTitle title="Chat" subtitle="WhatsApp" />

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
              Nueva Conversación
            </h5>
            <div className="row g-3">
              <div className="col-md-4">
                <input
                  type="text"
                  className="form-control"
                  placeholder="Teléfono (+5215512345678)"
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
                <button className="btn btn-light" onClick={() => setShowNewChat(false)}>
                  <i className="ri-close-line"></i>
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
                      onClick={() => setSelectedId(convo.id)}
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
                  <div>
                    <h5 className="mb-0">
                      {selectedConvo?.customerName || selectedConvo?.customerPhone}
                    </h5>
                    {selectedConvo?.customerName && (
                      <small className="text-muted">{selectedConvo.customerPhone}</small>
                    )}
                  </div>
                </div>

                {/* Messages */}
                <div className="flex-grow-1 overflow-auto p-3" style={{ backgroundColor: '#e5ddd5' }}>
                  {loadingMsgs ? (
                    <div className="text-center py-5">
                      <div className="spinner-border spinner-border-sm text-primary" />
                    </div>
                  ) : messages.length === 0 ? (
                    <div className="text-center py-5">
                      <p className="text-muted bg-white rounded p-2 d-inline-block">
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
                          <div
                            className="wa-message-bubble rounded p-2 position-relative"
                            style={{
                              backgroundColor: isOutbound ? '#dcf8c6' : '#ffffff',
                              boxShadow: '0 1px 1px rgba(0,0,0,.1)',
                            }}
                          >
                            <p className="mb-1" style={{ whiteSpace: 'pre-wrap', wordBreak: 'break-word' }}>
                              {msg.body}
                            </p>
                            <div className="text-end">
                              <small className="text-muted" style={{ fontSize: '0.7rem' }}>
                                {new Date(msg.sentAt).toLocaleTimeString('es-MX', {
                                  hour: '2-digit',
                                  minute: '2-digit',
                                })}
                                {isOutbound && (
                                  <i
                                    className={`ri-check-double-line ms-1 ${
                                      msg.status === 'Read'
                                        ? 'text-primary'
                                        : msg.status === 'Delivered'
                                          ? 'text-muted'
                                          : ''
                                    }`}
                                  ></i>
                                )}
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
    </>
  );
}
