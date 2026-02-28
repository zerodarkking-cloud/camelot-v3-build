import React, { useState, useEffect, useRef, useMemo } from 'react';
import { createClient } from '@supabase/supabase-js';
const { ipcRenderer } = window.require('electron');

// === 🔑 КЛЮЧИ SUPABASE ===
const SUPABASE_URL = 'https://qyjddyfsesyepdzhypyo.supabase.co'; 
const SUPABASE_KEY = 'sb_publishable_3o33vKD_ozwJx4okaXTNcg_WjdB9fOI';

const NOTIFICATION_SOUND = 'https://assets.mixkit.co/active_storage/sfx/2869/2869-preview.mp3';
const EMAIL_SUFFIX = '@camelot.team'; 
const formatUsername = (email) => email ? email.replace(EMAIL_SUFFIX, '') : '';
const supabase = createClient(SUPABASE_URL, SUPABASE_KEY);

// === ИКОНКИ ===
const Icons = 
{Emoji: () => ( <svg xmlns="http://www.w3.org/2000/svg" width="20" height="20" viewBox="0 0 16 16" fill="none" stroke="currentColor" className="text-current">
      <g transform="translate(108.63 -422.426)">
        <path d="M-100.63,438.426a8.009,8.009,0,0,1-8-8,8.008,8.008,0,0,1,8-8,8.009,8.009,0,0,1,8,8A8.009,8.009,0,0,1-100.63,438.426Zm0-14.884a6.892,6.892,0,0,0-6.884,6.884,6.892,6.892,0,0,0,6.884,6.884,6.891,6.891,0,0,0,6.883-6.884A6.891,6.891,0,0,0-100.63,423.542Z" fill="currentColor"></path>
      </g>
      <g transform="translate(108.63 -422.426)">
        <path d="M-100.63,433.961a4.568,4.568,0,0,1-3.424-1.712.557.557,0,0,1,.112-.781.557.557,0,0,1,.78.11,3.489,3.489,0,0,0,2.532,1.266,3.491,3.491,0,0,0,2.531-1.266.557.557,0,0,1,.781-.108.558.558,0,0,1,.111.779A4.565,4.565,0,0,1-100.63,433.961Z" fill="currentColor"></path>
      </g>
      <g transform="translate(108.63 -422.426)">
        <path d="M-102.855,428.991a.8.8,0,0,1-.8-.8.792.792,0,0,1,.792-.8h.01a.8.8,0,0,1,.8.8A.8.8,0,0,1-102.855,428.991Z" fill="currentColor"></path>
      </g>
      <g transform="translate(108.63 -422.426)">
        <path d="M-98.389,428.991a.8.8,0,0,1-.8-.8.792.792,0,0,1,.792-.8h.011a.8.8,0,0,1,.8.8A.8.8,0,0,1-98.389,428.991Z" fill="currentColor"></path>
      </g>
    </svg>
  ), 
  List: () => <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><line x1="8" y1="6" x2="21" y2="6"></line><line x1="8" y1="12" x2="21" y2="12"></line><line x1="8" y1="18" x2="21" y2="18"></line><line x1="3" y1="6" x2="3.01" y2="6"></line><line x1="3" y1="12" x2="3.01" y2="12"></line><line x1="3" y1="18" x2="3.01" y2="18"></line></svg>,
  Trash: () => <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><polyline points="3 6 5 6 21 6"></polyline><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"></path></svg>,
  Plus: () => <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><line x1="12" y1="5" x2="12" y2="19"></line><line x1="5" y1="12" x2="19" y2="12"></line></svg>,
  Translate: () => <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M5 8l6 6"></path><path d="M4 14h6"></path><path d="M2 5h12"></path><path d="M7 2h1"></path><path d="M22 22l-5-10-5 10"></path><path d="M14 18h6"></path></svg>,
  Send: () => <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><line x1="22" y1="2" x2="11" y2="13"></line><polygon points="22 2 15 22 11 13 2 9 22 2"></polygon></svg>,
  Clock: () => <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><circle cx="12" cy="12" r="10"></circle><polyline points="12 6 12 12 16 14"></polyline></svg>,
  Robot: () => <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><rect x="3" y="11" width="18" height="10" rx="2"></rect><circle cx="12" cy="5" r="2"></circle><path d="M12 7v4"></path><line x1="8" y1="16" x2="8" y2="16"></line><line x1="16" y1="16" x2="16" y2="16"></line></svg>,
  Check: () => <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><polyline points="20 6 9 17 4 12"></polyline></svg>,
  Settings: () => <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><circle cx="12" cy="12" r="3"></circle><path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1 0 2.83 2 2 0 0 1-2.83 0l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-2 2 2 2 0 0 1-2-2v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83 0 2 2 0 0 1 0-2.83l.06.06a1.65 1.65 0 0 0 .33-1.82 1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1-2-2 2 2 0 0 1 2-2h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 0 1 0-2.83 2 2 0 0 1 2.83 0l.06.06a1.65 1.65 0 0 0 1.82.33H9a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 2-2 2 2 0 0 1 2 2v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 0 2 2 0 0 1 0 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82V9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 2 2 2 2 0 0 1-2 2h-.09a1.65 1.65 0 0 0-1.51 1z"></path></svg>,
  Lock: () => <svg width="80" height="80" viewBox="0 0 24 24" fill="currentColor" stroke="none" className="drop-shadow-[0_0_10px_rgba(163,0,0,0.5)]"><path d="M12 2C9.243 2 7 4.243 7 7v3H6a2 2 0 0 0-2 2v8a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-8a2 2 0 0 0-2-2h-1V7c0-2.757-2.243-5-5-5zm0 2c1.654 0 3 1.346 3 3v3H9V7c0-1.654 1.346-3 3-3zm0 10c-1.103 0-2-.897-2-2s.897-2 2-2 2 .897 2 2-.897 2-2 2z"></path></svg>,
  Save: () => <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M19 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11l5 5v11a2 2 0 0 1-2 2z"></path><polyline points="17 21 17 13 7 13 7 21"></polyline><polyline points="7 3 7 8 15 8"></polyline></svg>,
  Pin: ({ filled }) => <svg width="14" height="14" viewBox="0 0 24 24" fill={filled ? "currentColor" : "none"} stroke="currentColor" strokeWidth="2" className={filled ? "text-gold" : "text-gray-600 group-hover:text-gold"}><path d="M16 3l-4 4-2.5-2.5L8 3l-5 5 1.5 1.5 2.5 2.5L3 16l3 3 10-13z"></path><line x1="16" y1="16" x2="21" y2="21"></line></svg>,
  Users: () => <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"></path><circle cx="9" cy="7" r="4"></circle><path d="M23 21v-2a4 4 0 0 0-3-3.87"></path><path d="M16 3.13a4 4 0 0 1 0 7.75"></path></svg>,
  Emblem: () => <svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5"><path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z" fill="currentColor" fillOpacity="0.1"/><path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"/><path d="M12 2v20" strokeWidth="2"/><path d="M8 8h8" strokeWidth="2"/></svg>,
  Help: () => <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><circle cx="12" cy="12" r="10"></circle><path d="M9.09 9a3 3 0 0 1 5.83 1c0 2-3 3-3 3"></path><line x1="12" y1="17" x2="12.01" y2="17"></line></svg>
};
const QUICK_EMOJIS = ['😀','😂','🥰','😍','😘','😜','😎','🥺','😭','😡','🔥','👍','❤️','✨','💦','🍑','😈','🍆','💋','💰'];
// === КОМПОНЕНТ ПОДСКАЗКИ ===
const HelpModal = ({ activeTab, onClose }) => {
    let title = "";
    let items = [];

    if (activeTab === 'WORK_ZONE') {
        title = "ГАЙД: РАБОЧАЯ ЗОНА";
        items = [
            "1. Выберите модель в меню сверху, чтобы загрузить чаты.",
            "2. Кнопка 'НАЧАТЬ СМЕНУ' обязательна для работы (без нее чат не отправится).",
            "3. Чтобы перевести сообщение клиента: наведите на него и нажмите иконку перевода.",
            "4. Чтобы ответить с переводом: напишите на русском, поставьте галочку 'ПЕРЕВОД' и отправьте.",
            "5. Робот 🤖 генерирует ответы через ИИ (нажмите для подсказки).",
            "6. Скрепка/Камера — для отправки фото и видео (работает Drag & Drop)."
        ];
    } else if (activeTab === 'AUTO_PILOT') {
        title = "ГАЙД: АВТОПИЛОТ";
        items = [
            "1. Вставьте список ссылок на профили или ID пользователей в левое поле.",
            "2. Заполните шаблоны сообщений (справа). Система будет чередовать их.",
            "3. Нажмите 'ЗАПУСТИТЬ'. Бот будет заходить, ждать паузу и отправлять сообщение.",
            "4. Не закрывайте вкладку во время работы.",
            "5. 'Экстренная остановка' мгновенно прерывает цикл."
        ];
    } else if (activeTab === 'BACKSTAGE') {
        title = "ГАЙД: БРАУЗЕР (РУЧНОЙ РЕЖИМ)";
        items = [
            "1. Используется для сложных действий, которые нельзя сделать в чате.",
            "2. Нажмите на ЗАМОК, чтобы войти в аккаунт выбранной модели.",
            "3. Куки сохраняются автоматически при выходе.",
            "4. Если сайт завис — нажмите кнопку 'Обновить сессию' или перезайдите.",
            "5. Лорды и Короли могут заходить без ограничений."
        ];
    } else {
        return null;
    }

    return (
        <div className="absolute inset-0 z-50 bg-black/80 backdrop-blur-sm flex items-center justify-center animate-fade-in" onClick={onClose}>
            <div className="bg-[#121212] border border-gold/30 rounded-2xl p-8 max-w-lg shadow-[0_0_50px_rgba(212,175,55,0.1)] relative" onClick={e => e.stopPropagation()}>
                <button onClick={onClose} className="absolute top-4 right-4 text-gray-500 hover:text-white transition-colors">✕</button>
                <div className="text-gold font-bold text-lg tracking-widest mb-6 flex items-center gap-3">
                    <Icons.Help /> {title}
                </div>
                <ul className="space-y-4">
                    {items.map((item, i) => (
                        <li key={i} className="text-gray-300 text-sm font-mono leading-relaxed flex gap-3">
                            <span className="text-blood-red font-bold">{i + 1}.</span>
                            {item}
                        </li>
                    ))}
                </ul>
                <div className="mt-8 pt-4 border-t border-white/10 text-center text-[10px] text-gray-600 uppercase tracking-widest">
                    CAMELOT SECURITY PROTOCOLS
                </div>
            </div>
        </div>
    );
};
// === ИЗОЛИРОВАННЫЙ КОМПОНЕНТ ЗАМЕТОК ===
// Защищает поле ввода от постоянных перерисовок интерфейса при активных чатах
const ClientNotes = ({ currentModel, selectedChat, notesMap, setNotesMap }) => {
    const key = `${currentModel?.id}_${selectedChat?.name}`;
    const [localText, setLocalText] = useState(notesMap[key] || '');

    // Обновляем текст ТОЛЬКО при клике на нового клиента (смене чата)
    useEffect(() => {
        setLocalText(notesMap[key] || '');
    }, [key]); 

    // Сохраняем текст в фоне без блокировки клавиатуры
    const handleChange = (e) => {
        const text = e.target.value;
        setLocalText(text); // Локально текст печатается мгновенно
        
        // Тихо обновляем глобальный стейт и память браузера
        const safeNotes = typeof notesMap === 'object' && notesMap !== null ? notesMap : {};
        const newNotes = { ...safeNotes, [key]: text };
        setNotesMap(newNotes);
        localStorage.setItem('camelot_notes', JSON.stringify(newNotes));
    };

    return (
        <textarea 
            className="flex-1 w-full bg-[#151515] border border-red-900/20 p-3 rounded-xl text-gray-200 text-xs outline-none focus:border-blood-red transition-all resize-none custom-scrollbar font-mono shadow-inner"
            placeholder="Запиши сюда фетиши, особенности, щедрость фаната..."
            value={localText}
            onChange={handleChange}
        />
    );
};
function App() {
  const [session, setSession] = useState(null);
  const [userRole, setUserRole] = useState(null); 
  const [userId, setUserId] = useState(null);
  const [authLoading, setAuthLoading] = useState(true);
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');

  // Admin & Work
  const [adminTab, setAdminTab] = useState('USERS'); 
  const [newUserUsername, setNewUserUsername] = useState('');
  const [newUserPass, setNewUserPass] = useState('');
  const [newUserRole, setNewUserRole] = useState('knight');
  const [adminMsg, setAdminMsg] = useState('');
  const [models, setModels] = useState([]);
  const [mySubordinates, setMySubordinates] = useState([]); 
  const [workLogs, setWorkLogs] = useState([]);
  const [currentModel, setCurrentModel] = useState(null); 
  
  // UI State
  const [showHelp, setShowHelp] = useState(false); // 🔥 Состояние окна помощи

  // Model Config State
  const [newModelName, setNewModelName] = useState('');
  const [newModelPrompt, setNewModelPrompt] = useState('Ты дерзкая и веселая модель. Отвечай коротко.');
  const [newModelProvider, setNewModelProvider] = useState('openrouter'); 
  const [editingModelId, setEditingModelId] = useState(null);
  const [editPrompt, setEditPrompt] = useState('');
  const [editProvider, setEditProvider] = useState('');
  const [assigningModelId, setAssigningModelId] = useState(null); 

  const [isWorking, setIsWorking] = useState(false);
  const [currentWorkId, setCurrentWorkId] = useState(null);
  const [workStartTime, setWorkStartTime] = useState(null);
  const [isBrowserActive, setIsBrowserActive] = useState(false);

  const [activeTab, setActiveTab] = useState('WORK_ZONE');
  const [status, setStatus] = useState({ text: 'INIT', color: 'text-gray-500' });
  const [rawChats, setRawChats] = useState([]); 
  const [history, setHistory] = useState([]);
  const [selectedChat, setSelectedChat] = useState(null);
  const [inputText, setInputText] = useState('');
  const [showEmojiPicker, setShowEmojiPicker] = useState(false);
  const [isTranslateOn, setIsTranslateOn] = useState(false);
  const [unreadMap, setUnreadMap] = useState({});
  const [aiLoading, setAiLoading] = useState(false);

  const [pinnedMap, setPinnedMap] = useState({}); 
  const [notesMap, setNotesMap] = useState(() => JSON.parse(localStorage.getItem('camelot_notes')) || {});

  const handleNoteChange = (text) => {
      if (!currentModel || !selectedChat) return;
      const key = `${currentModel.id}_${selectedChat.name}`;
      
      // 🔥 МАГИЯ: Используем prev для защиты от быстрых перерисовок интерфейса
      setNotesMap(prev => {
          const safePrev = typeof prev === 'object' && prev !== null ? prev : {};
          const newNotes = { ...safePrev, [key]: text };
          localStorage.setItem('camelot_notes', JSON.stringify(newNotes));
          return newNotes;
      });
  };

  // Auto Pilot
  const [rawLinks, setRawLinks] = useState("");
  const [targets, setTargets] = useState([]);
  const [templates, setTemplates] = useState(() => JSON.parse(localStorage.getItem('camelot_templates')) || ["", "", "", "", ""]);
  const [isAutoRunning, setIsAutoRunning] = useState(false);
  const [autoLog, setAutoLog] = useState([]); 
  const [sentHistory, setSentHistory] = useState([]); 
  const [currentAutoIndex, setCurrentAutoIndex] = useState(-1);
  const [resumeIndex, setResumeIndex] = useState(0); 
  const [globalTemplateIndex, setGlobalTemplateIndex] = useState(0); 

  const webviewRef = useRef(null);
  const messagesEndRef = useRef(null);
  const scrollContainerRef = useRef(null);
  const lastHistoryLen = useRef(0);
  const isRunningRef = useRef(false);
  const latestChatState = useRef({ isChatAvailable: false, history: [] });
  const audioRef = useRef(new Audio(NOTIFICATION_SOUND)); 

  useEffect(() => {
    supabase.auth.getSession().then(({ data: { session } }) => initSession(session));
    const { data: { subscription } } = supabase.auth.onAuthStateChange((_event, session) => initSession(session));
    return () => subscription.unsubscribe();
  }, []);

  const initSession = async (session) => {
      try {
          setSession(session);
          if(session) {
              setUserId(session.user.id);
              const savedPins = JSON.parse(localStorage.getItem(`camelot_pins_${session.user.id}`)) || {};
              setPinnedMap(savedPins);
              
              const { data, error } = await supabase.from('profiles').select('role').eq('id', session.user.id).single();
              
              if(data) {
                  setUserRole(data.role);
                  await fetchModels(session.user.id, data.role);
                  await checkActiveWorkSession(session.user.id);
                  if(data.role === 'king' || data.role === 'lord') {
                      await fetchSubordinates(session.user.id, data.role);
                  }
              } else {
                  // Если профиль не найден (ошибка или удален), сбрасываем сессию
                  console.error("Профиль не найден, сброс сессии:", error);
                  await supabase.auth.signOut();
                  setSession(null);
                  setUserRole(null);
              }
          } else { 
              setUserRole(null); 
          }
      } catch (err) {
          console.error("Скрытая ошибка синхронизации:", err);
          // В случае критической ошибки сети также сбрасываем состояние
          setUserRole(null);
      } finally {
          // Даем React миллисекунду на применение стейтов перед снятием экрана загрузки
          setTimeout(() => {
              setAuthLoading(false);
          }, 100);
      }
    };
  const toggleAssignment = async (model, targetId, type) => {
      const field = type === 'lord' ? 'lord_ids' : 'knight_ids';
      let currentIds = model[field] || [];
      
      let newIds;
      if (currentIds.includes(targetId)) {
          newIds = currentIds.filter(id => id !== targetId);
      } else {
          newIds = [...currentIds, targetId];
      }

      const { error } = await supabase.from('models').update({ [field]: newIds }).eq('id', model.id);
      if (!error) {
          fetchModels(userId, userRole);
      } else {
          alert("Ошибка назначения: " + error.message);
      }
  };

  const togglePin = (e, chatName) => {
      e.stopPropagation();
      if (!currentModel) return;
      const modelId = currentModel.id;
      setPinnedMap(prev => {
          const modelPins = prev[modelId] || [];
          const isPinned = modelPins.includes(chatName);
          let newPins;
          if (isPinned) { newPins = modelPins.filter(n => n !== chatName); } else { newPins = [...modelPins, chatName]; }
          const newMap = { ...prev, [modelId]: newPins };
          localStorage.setItem(`camelot_pins_${userId}`, JSON.stringify(newMap));
          return newMap;
      });
  };

  const chats = useMemo(() => {
      if (!rawChats || !currentModel) return rawChats;
      const myPins = pinnedMap[currentModel.id] || [];
      return [...rawChats].sort((a, b) => {
          const isPinnedA = myPins.includes(a.name);
          const isPinnedB = myPins.includes(b.name);
          if (isPinnedA && !isPinnedB) return -1;
          if (!isPinnedA && isPinnedB) return 1;
          if (a.isUnreadSite && !b.isUnreadSite) return -1;
          if (!a.isUnreadSite && b.isUnreadSite) return 1;
          return a.index - b.index;
      });
  }, [rawChats, pinnedMap, currentModel]);

  const fetchSubordinates = async (uid, role) => {
      let query = supabase.from('profiles').select('id, email, role');
      if (role === 'lord') query = query.eq('role', 'knight').eq('owner_id', uid);
      const { data } = await query;
      if(data) setMySubordinates(data); 
  };

  const fetchModels = async (uid, role) => {
      let query = supabase.from('models').select('*');
      
      if (role === 'knight') {
          query = query.contains('knight_ids', [uid]);
      } else if (role === 'lord') {
          query = query.contains('lord_ids', [uid]);
      }

      const { data } = await query;
      if (data) setModels(data);
  };

  const checkActiveWorkSession = async (uid) => { 
      // Убираем .single(), ищем все незакрытые смены пользователя, сортируем от новых к старым
      const { data } = await supabase
          .from('work_logs')
          .select('*')
          .eq('user_id', uid)
          .is('end_time', null)
          .order('start_time', { ascending: false }); 

      if (data && data.length > 0) { 
          // Берем самую последнюю смену и продолжаем её
          const currentSession = data[0];
          setIsWorking(true); 
          setCurrentWorkId(currentSession.id); 
          setWorkStartTime(new Date(currentSession.start_time)); 

          // 🔥 МАГИЯ: Если в базе зависли другие старые смены-дубли, автоматически их закрываем
          if (data.length > 1) {
              const orphanedIds = data.slice(1).map(s => s.id);
              await supabase.from('work_logs')
                  .update({ end_time: new Date().toISOString(), duration_minutes: 0, profit: 0 })
                  .in('id', orphanedIds);
          }
      } 
  };

  const startWorkShift = async () => { 
      // Убрали start_balance: 0
      const { data, error } = await supabase.from('work_logs').insert({ 
          user_id: userId
      }).select().single(); 
      
      if (error) {
          console.error("Ошибка БД при старте смены:", error);
          alert("ОШИБКА БАЗЫ ДАННЫХ:\n" + error.message);
          return;
      }

      if (data) { 
          setIsWorking(true); 
          setCurrentWorkId(data.id); 
          setWorkStartTime(new Date()); 
      } 
  };

  const scanEarnings = async () => {
      if(!webviewRef.current) return 0;
      
      const script = `
        (function() {
            const rows = document.querySelectorAll('table tbody tr');
            if(rows.length === 0) return { error: "Таблица не найдена" };
            let sales = [];
            rows.forEach(row => {
                const cols = row.querySelectorAll('td');
                if(cols.length > 1) {
                    let dateText = cols[0].innerText.trim().split('\\n')[0]; 
                    let amountText = cols[cols.length-1].innerText.trim(); 
                    sales.push({ date: dateText, amount: amountText });
                }
            });
            return { sales: sales, url: window.location.href };
        })();
      `;
      
      try {
          const result = await webviewRef.current.executeJavaScript(script);
          if(result.error) {
              alert("⚠️ Ошибка: " + result.error + "\n\nОткройте вкладку 'View My Earnings'!");
              return 0;
          }
          if(!result.url.includes('Earnings') && !result.url.includes('earnings')) {
              alert("⚠️ Вы должны быть на странице Earnings (Мой заработок)!");
              return 0;
          }

          let totalProfit = 0;
          let salesCount = 0;
          const shiftStart = new Date(workStartTime);

          result.sales.forEach(s => {
              let cleanDate = s.date.replace('at ', '').replace('am', ' AM').replace('pm', ' PM');
              const saleDate = new Date(cleanDate);
              
              if(!isNaN(saleDate) && saleDate > shiftStart) {
                  const money = parseFloat(s.amount.replace('$',''));
                  if(!isNaN(money)) {
                      totalProfit += money;
                      salesCount++;
                  }
              }
          });
          
          return { profit: totalProfit, count: salesCount };

      } catch (e) {
          console.error(e);
          alert("Не удалось прочитать таблицу. Проверьте, что страница загружена.");
          return 0;
      }
  };

  const stopWorkShift = async () => { 
      if (!currentWorkId) return; 
      
      const manualInput = window.confirm("🤖 Использовать АВТО-РАСЧЕТ по таблице ManyVids?\n\nОК - Сканировать экран (Откройте 'View My Earnings')\nОтмена - Ввести сумму вручную");
      
      let profit = 0;
      
      if(manualInput) {
          setActiveTab('BACKSTAGE'); 
          alert("⏳ Откройте страницу 'Earnings' в браузере и нажмите ОК для старта сканирования.");
          
          const scanRes = await scanEarnings();
          if(scanRes && typeof scanRes.profit === 'number') {
              profit = scanRes.profit;
              alert(`✅ НАЙДЕНО:\nНовых продаж: ${scanRes.count}\nСумма: $${profit.toFixed(2)}`);
          } else {
              const fallback = window.prompt("⚠️ Сканирование не удалось. Введите прибыль вручную ($):", "0");
              profit = parseFloat(fallback) || 0;
          }
      } else {
          const input = window.prompt("💰 Введите ЗАРАБОТОК за эту смену ($):", "0");
          if (input === null) return; 
          profit = parseFloat(input.replace(',', '.')) || 0;
      }

      const now = new Date(); 
      const duration = Math.round((now - workStartTime) / 60000); 

      await supabase.from('work_logs').update({ 
          end_time: now.toISOString(), 
          duration_minutes: duration,
          profit: profit
      }).eq('id', currentWorkId); 

      setIsWorking(false); 
      setCurrentWorkId(null); 
      setWorkStartTime(null); 
  };

  const fetchStats = async () => { 
      let query = supabase.from('work_logs').select('*, profiles!inner(email, owner_id)'); 
      if (userRole === 'lord') {
          query = query.eq('profiles.owner_id', userId);
      }
      const { data } = await query.order('start_time', { ascending: false }).limit(50); 
      if (data) setWorkLogs(data); 
  };
  
  const handleLogin = async (e) => { e.preventDefault(); setAuthLoading(true); const cleanLogin = username.trim(); const fakeEmail = cleanLogin.includes('@') ? cleanLogin : cleanLogin + EMAIL_SUFFIX; const { error } = await supabase.auth.signInWithPassword({ email: fakeEmail, password }); if (error) alert('Ошибка входа: ' + error.message); setAuthLoading(false); };
  
  const handleLogout = async () => { 
      try {
          // Если сотрудник выходит из аккаунта, не закрыв смену, закрываем её принудительно (штрафной 0)
          if(isWorking && currentWorkId) { 
              const now = new Date(); 
              const duration = Math.round((now - workStartTime) / 60000); 
              await supabase.from('work_logs').update({ 
                  end_time: now.toISOString(), 
                  duration_minutes: duration, 
                  profit: 0 
              }).eq('id', currentWorkId); 
          }
          await ipcRenderer.invoke('logout-session'); 
          await supabase.auth.signOut(); 
      } catch (e) {
          console.error("Ошибка при выходе", e);
      } finally {
          setUsername(''); 
          setPassword(''); 
          window.location.reload(); 
      }
  };

  const deleteUser = async (id, email) => { if(!window.confirm(`Удалить сотрудника ${formatUsername(email)}?`)) return; const { error } = await supabase.from('profiles').delete().eq('id', id); if(error) alert("Ошибка удаления: " + error.message); else { alert("Сотрудник удален."); fetchSubordinates(userId, userRole); } };
  const deleteModel = async (id, name) => { if(!window.confirm(`Удалить модель ${name}?`)) return; const { error } = await supabase.from('models').delete().eq('id', id); if(error) alert("Ошибка удаления: " + error.message); else { alert("Модель удалена."); fetchModels(userId, userRole); } };
  
  const createSubAccount = async () => { if(!newUserUsername || !newUserPass) return; setAdminMsg('Создание...'); const cleanLogin = newUserUsername.trim(); const fakeNewEmail = cleanLogin.includes('@') ? cleanLogin : cleanLogin + EMAIL_SUFFIX; const tempSupabase = createClient(SUPABASE_URL, SUPABASE_KEY, { auth: { persistSession: false, autoRefreshToken: false, detectSessionInUrl: false } }); const { data, error } = await tempSupabase.auth.signUp({ email: fakeNewEmail, password: newUserPass }); if (error) setAdminMsg(`Ошибка: ${error.message}`); else if (data.user) { const owner = userRole === 'lord' ? userId : null; const { error: roleError } = await supabase.from('profiles').update({ role: newUserRole, owner_id: owner }).eq('id', data.user.id); if(!roleError) { setAdminMsg(`✅ ${formatUsername(fakeNewEmail)} создан!`); setNewUserUsername(''); setNewUserPass(''); fetchSubordinates(userId, userRole); } } };
  
  const createModel = async () => { 
      if(!newModelName) return; 
      
      let cookies = [];
      try {
          cookies = await ipcRenderer.invoke('save-session-cookies');
      } catch (e) {
          console.log("Cookies not found, creating empty model");
      }

      const { error } = await supabase.from('models').insert({ 
          name: newModelName, 
          cookies: cookies, 
          owner_id: userId, 
          system_prompt: newModelPrompt, 
          ai_provider: newModelProvider,
          lord_ids: userRole === 'lord' ? [userId] : []
      }); 

      if(!error) { 
          setAdminMsg('✅ Модель добавлена'); 
          setNewModelName(''); 
          fetchModels(userId, userRole); 
      } else { 
          setAdminMsg('Ошибка: ' + error.message); 
      } 
  };

  const assignModel = async (modelId, targetId, type) => { const update = type === 'lord' ? { assigned_lord_id: targetId } : { assigned_knight_id: targetId }; await supabase.from('models').update(update).eq('id', modelId); fetchModels(userId, userRole); };
  const startEditing = (model) => { setEditingModelId(model.id); setEditPrompt(model.system_prompt || ''); setEditProvider(model.ai_provider || 'openrouter'); };
  const saveModelSettings = async (id) => { const { error } = await supabase.from('models').update({ system_prompt: editPrompt, ai_provider: editProvider }).eq('id', id); if (!error) { setEditingModelId(null); fetchModels(userId, userRole); } else { alert('Ошибка обновления: ' + error.message); } };
  const updateSession = async () => { if (!currentModel) return; const cookies = await ipcRenderer.invoke('save-session-cookies'); if (cookies.length > 0) { const { error } = await supabase.from('models').update({ cookies: cookies }).eq('id', currentModel.id); if (!error) { alert('✅ СЕССИЯ ОБНОВЛЕНА!'); setCurrentModel({ ...currentModel, cookies: cookies }); } else { alert('Ошибка сохранения: ' + error.message); } } else { alert('⚠️ Не удалось захватить куки.'); } };
  const selectModel = (model) => { setCurrentModel(model); setIsBrowserActive(false); setActiveTab('BACKSTAGE'); };
  
  const forceEnterBrowser = async () => { 
      if(!currentModel && userRole !== 'king') return; 
      
      if (currentModel) {
          addLog(`🔐 Вход в систему: ${currentModel.name}`); 
          await ipcRenderer.invoke('load-session-cookies', currentModel.cookies); 
      } else {
          addLog(`👑 MASTER ACCESS (NO MODEL)`);
          await ipcRenderer.invoke('load-session-cookies', []);
      }

      setIsBrowserActive(true); 
      setTimeout(() => { if(webviewRef.current) webviewRef.current.loadURL("https://www.manyvids.com/"); }, 500); 
  };

  useEffect(() => { localStorage.setItem('camelot_templates', JSON.stringify(templates)); }, [templates]);
  
  useEffect(() => {
    const handleData = (event, data) => {
      latestChatState.current = { isChatAvailable: data.isChatAvailable, history: data.history };
      if (data.status) setStatus(data.status === 'ONLINE' ? { text: 'ONLINE', color: 'text-green-500' } : { text: 'LOGIN NEEDED', color: 'text-orange-500' });
      
      if (!isAutoRunning && data.chats && Array.isArray(data.chats)) {
          let shouldPlaySound = false;
          setRawChats(data.chats);

          setUnreadMap(prev => {
              const newMap = { ...prev };
              let hasChanges = false;
              data.chats.forEach(c => { 
                  if (c.isUnreadSite && !newMap[c.name]) { newMap[c.name] = true; hasChanges = true; }
              });
              if (hasChanges && shouldPlaySound) {
                  audioRef.current.play().catch(e => console.log("Audio play error:", e));
              }
              return newMap;
          });
      }

      if (data.history && Array.isArray(data.history)) {
          const isSame = history.length === data.history.length && 
                         data.history.length > 0 && 
                         history[history.length-1].html === data.history[data.history.length-1].html;
          
          if (!isSame) {
              if (data.history.length > lastHistoryLen.current || (selectedChat && data.history.length !== history.length)) {
                  setTimeout(() => messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' }), 100);
              }
              lastHistoryLen.current = data.history.length;
              setHistory(data.history);
              if (!isAutoRunning && selectedChat && selectedChat.name && typeof data.lastMessageIsMine !== 'undefined') {
                  setUnreadMap(prev => ({ ...prev, [selectedChat.name]: !data.lastMessageIsMine }));
              }
          }
      }
    };
    ipcRenderer.on('ui-update', handleData);
    return () => ipcRenderer.removeListener('ui-update', handleData);
  }, [selectedChat, isAutoRunning, history.length]);

  useEffect(() => { const wv = webviewRef.current; if (!wv) return; const handleNavigate = (e) => { const url = e.url; if (url.includes('manyvids.com/login') || url.includes('manyvids.com/signin')) { wv.executeJavaScript(`document.body.innerHTML = '<div style="background:#0a0a0a;color:#a30000;height:100vh;display:flex;justify-content:center;align-items:center;font-family:sans-serif;flex-direction:column;"><h1>⛔ ДОСТУП ЗАПРЕЩЕН</h1><p style="color:#666">Вход возможен только через выбор модели в меню.</p></div>';`); } }; const handleDomReady = () => { wv.insertCSS(`a[href*="logout"], button[onclick*="logout"], .mv_logout { display: none !important; } footer, .footer { display: none !important; }`); }; wv.addEventListener('did-navigate', handleNavigate); wv.addEventListener('dom-ready', handleDomReady); return () => { if (wv) { wv.removeEventListener('did-navigate', handleNavigate); wv.removeEventListener('dom-ready', handleDomReady); } }; }, []);
  const generateAiReply = async () => { if (!selectedChat || !currentModel) { if(!currentModel) alert("⚠️ Выберите модель!"); return; } setAiLoading(true); const response = await ipcRenderer.invoke('ask-ai', { history: history, prompt: currentModel.system_prompt, provider: currentModel.ai_provider }); setInputText(response); setAiLoading(false); };
  const translateMessage = async (index, text) => { try { const translatedText = await ipcRenderer.invoke('translate', { text: text.replace(/<[^>]*>?/gm, ''), lang: 'en|ru' }); setHistory(prev => { const newHistory = [...prev]; newHistory[index] = { ...newHistory[index], html: translatedText, translated: true }; return newHistory; }); } catch (error) { console.error("Ошибка перевода:", error); } };
  const handleChatClick = (chat) => { if (!chat) return; setSelectedChat(chat); setHistory([]); lastHistoryLen.current = 0; webviewRef.current.send('click-chat', chat.index); };
  const sendMessage = async () => { if(!isWorking && userRole === 'knight') { alert("⚠️ НАЧНИТЕ СМЕНУ!"); return; } if (!inputText.trim() || !selectedChat) return; let finalText = inputText; if (isTranslateOn) try { finalText = await ipcRenderer.invoke('translate', { text: inputText, lang: 'ru|en' }); } catch (e) {} setHistory(prev => [...prev, { html: finalText, isMine: true, time: 'Now' }]); setUnreadMap(prev => ({ ...prev, [selectedChat.name]: false })); lastHistoryLen.current += 1; setTimeout(() => messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' }), 50); webviewRef.current.send('send-message', finalText); setInputText(''); };
  const handleAction = (action) => webviewRef.current.send('trigger-action', action);
  const handleKeyDown = (e) => { if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); sendMessage(); } };
  const pendingCount = rawLinks.split('\n').filter(l => l.trim().length > 0).length;
  const getRandomTime = (min, max) => Math.floor(Math.random() * (max - min + 1) + min) * 1000;
  const sleep = (ms) => new Promise(r => setTimeout(r, ms));
  const loadTargets = () => { const lines = rawLinks.split('\n').map(l => l.trim()).filter(l => l.length > 0); const uniqueLines = [...new Set(lines)]; const newTargets = uniqueLines.map((link, i) => ({ id: i, link: link, status: 'WAITING' })); setTargets(newTargets); setRawLinks(""); setResumeIndex(0); setGlobalTemplateIndex(0); setSentHistory([]); addLog(`📂 Загружено ${newTargets.length} целей.`); };
  const clearTargets = () => { setTargets([]); setResumeIndex(0); setGlobalTemplateIndex(0); setCurrentAutoIndex(-1); setSentHistory([]); };
  const addLog = (msg) => setAutoLog(prev => [`[${new Date().toLocaleTimeString()}] ${msg}`, ...prev]);
  const startAutoPilot = async () => { if(!isWorking && userRole === 'knight') { alert("⚠️ НАЧНИТЕ СМЕНУ!"); return; } if (isAutoRunning) return; if (targets.length === 0) { addLog("❌ Нет целей!"); return; } const validTemplates = templates.filter(t => t.trim().length > 0); if (validTemplates.length === 0) { addLog("❌ Нет шаблонов!"); return; } setIsAutoRunning(true); isRunningRef.current = true; setActiveTab('BACKSTAGE'); let tempGlobalIndex = globalTemplateIndex; const startIndex = resumeIndex; if (startIndex > 0) addLog(`▶ Возобновление с #${startIndex + 1}`); else addLog(`🚀 Старт. Целей: ${targets.length}`); for (let i = startIndex; i < targets.length; i++) { if (!isRunningRef.current || !webviewRef.current) { addLog("⏸️ Пауза."); setResumeIndex(i); setGlobalTemplateIndex(tempGlobalIndex); break; } setCurrentAutoIndex(i); const target = targets[i]; let url = target.link; if (!url.includes('http')) url = `https://www.manyvids.com/inbox/messages/${target.link}`; try { await webviewRef.current.loadURL(url); } catch (e) { addLog(`❌ Ошибка URL: ${target.link}`); continue; } addLog(`User ${i + 1}: Переход... (Чтение...)`); const readTime = getRandomTime(4, 8); await sleep(readTime); if(!isRunningRef.current) { setResumeIndex(i); setGlobalTemplateIndex(tempGlobalIndex); break; } if (latestChatState.current.isChatAvailable) { const tplIndex = tempGlobalIndex % validTemplates.length; const templateToSend = validTemplates[tplIndex]; webviewRef.current.send('send-message', templateToSend); addLog(`✅ Отправлено: Шаблон #${tplIndex + 1}`); setSentHistory(prev => [{ user: target.link.replace('https://www.manyvids.com/inbox/messages/', '').substring(0, 15) + '...', template: `Tpl #${tplIndex + 1}`, time: new Date().toLocaleTimeString() }, ...prev]); tempGlobalIndex++; const coolDown = getRandomTime(3, 6); addLog(`⏳ Остывание... ${coolDown/1000}с`); await sleep(coolDown); } else { addLog(`⚠️ Недоступен/Блок. Пропуск.`); await sleep(2000); } } if (isRunningRef.current) { setIsAutoRunning(false); isRunningRef.current = false; setResumeIndex(0); setGlobalTemplateIndex(0); setCurrentAutoIndex(-1); addLog("🏁 Рассылка завершена!"); setActiveTab('WORK_ZONE'); } else { setIsAutoRunning(false); } };
  const stopAutoPilot = () => { isRunningRef.current = false; addLog("🛑 Стоп..."); };

  if (authLoading) {
      return (
          <div className="flex h-screen w-screen royal-grid-bg items-center justify-center flex-col gap-5 text-blood-red font-sans animate-pulse">
              <div className="transform scale-150 drop-shadow-[0_0_15px_rgba(163,0,0,0.5)]"><Icons.Emblem /></div>
              <div className="tracking-[0.3em] font-bold text-sm mt-4 uppercase">Синхронизация серверов...</div>
          </div>
      );
  }
      // ... дальше идет твоя форма логина ...
  if (!session) {
      return (
          <div className="flex h-screen w-screen royal-grid-bg items-center justify-center text-gray-200 font-sans animate-fade-in">
              <div className="absolute inset-0 bg-gradient-to-b from-transparent via-black/20 to-black/70 pointer-events-none"></div>
              <form onSubmit={handleLogin} className="w-96 p-8 glass-panel rounded-2xl shadow-[0_0_30px_rgba(0,0,0,0.5)] flex flex-col gap-5 relative z-10 border border-red-900/30">
                  <div className="text-center flex flex-col items-center gap-2 mb-4">
                      <div className="text-blood-red transform scale-150"><Icons.Emblem /></div>
                      <div className="font-bold text-blood-red text-2xl tracking-[0.2em] mt-2">CAMELOT</div>
                      <div className="text-xs text-gray-500 tracking-wider">SECURE ACCESS TERMINAL</div>
                  </div>
                  <div className="flex flex-col gap-1">
                    <label className="text-[10px] text-gray-500 uppercase font-bold tracking-wider ml-1">Логин</label>
                    <input type="text" className="bg-[#0a0a0a] border border-[#333] p-3 rounded-lg text-white outline-none focus:border-blood-red transition-all focus:shadow-[0_0_10px_rgba(163,0,0,0.2)]" value={username} onChange={e => setUsername(e.target.value)} />
                  </div>
                  <div className="flex flex-col gap-1">
                    <label className="text-[10px] text-gray-500 uppercase font-bold tracking-wider ml-1">Пароль</label>
                    <input type="password" className="bg-[#0a0a0a] border border-[#333] p-3 rounded-lg text-white outline-none focus:border-blood-red transition-all focus:shadow-[0_0_10px_rgba(163,0,0,0.2)]" value={password} onChange={e => setPassword(e.target.value)} />
                  </div>
                  <button type="submit" disabled={authLoading} className="bg-gradient-to-r from-red-900 to-[#a30000] hover:from-[#a30000] hover:to-red-600 text-white font-bold py-3 rounded-lg transition-all shadow-lg hover:shadow-[0_0_20px_#a30000] tracking-wider">ВОЙТИ</button>
              </form>
          </div>
      );
  }

  return (
    <div className="flex flex-col h-screen w-screen royal-grid-bg text-gray-200 font-sans animate-fade-in relative">
      {/* МОДАЛЬНОЕ ОКНО ПОМОЩИ */}
      {showHelp && <HelpModal activeTab={activeTab} onClose={() => setShowHelp(false)} />}

      <div className="absolute inset-0 bg-gradient-to-b from-transparent via-transparent to-black/40 pointer-events-none z-0"></div>
      <style>{` .emoji-img { width: 18px; height: 18px; display: inline-block; vertical-align: middle; margin: 0 2px; } `}</style>
      
      <header className="h-14 header-integrated flex items-center justify-between px-6 select-none relative z-20">
        <div className="flex items-center gap-6">
          <div className="font-bold text-blood-red tracking-[0.15em] flex items-center gap-3 text-lg">
            <Icons.Emblem /> CAMELOT 
            <span className="text-[10px] text-gray-400 bg-black/50 px-2 py-0.5 rounded border border-[#333]">{userRole?.toUpperCase()}</span>
          </div>
          <div className="flex bg-[#0a0a0a]/50 rounded-lg p-1 gap-1 border border-white/5 backdrop-blur-md">
            <button onClick={() => setActiveTab('WORK_ZONE')} className={`px-4 py-1.5 text-[11px] font-bold rounded-md transition-all ${activeTab === 'WORK_ZONE' ? 'bg-gradient-to-r from-red-900 to-[#a30000] text-white shadow-sm' : 'text-gray-500 hover:text-gray-300'}`}>РАБОЧАЯ ЗОНА</button>
            <button onClick={() => setActiveTab('AUTO_PILOT')} className={`px-4 py-1.5 text-[11px] font-bold rounded-md transition-all ${activeTab === 'AUTO_PILOT' ? 'bg-gradient-to-r from-red-900 to-[#a30000] text-white shadow-sm' : 'text-gray-500 hover:text-gray-300'}`}>АВТОПИЛОТ</button>
            <button onClick={() => setActiveTab('BACKSTAGE')} className={`px-4 py-1.5 text-[11px] font-bold rounded-md transition-all ${activeTab === 'BACKSTAGE' ? 'bg-gradient-to-r from-red-900 to-[#a30000] text-white shadow-sm' : 'text-gray-500 hover:text-gray-300'}`}>БРАУЗЕР</button>
            {(userRole === 'king' || userRole === 'lord') && (
                <button onClick={() => { setActiveTab('ADMIN'); fetchStats(); }} className={`px-4 py-1.5 text-[11px] font-bold rounded-md transition-all ${activeTab === 'ADMIN' ? 'bg-gradient-to-r from-yellow-700 to-yellow-600 text-white shadow-[0_0_10px_#ca8a04]' : 'text-yellow-700/50 hover:text-yellow-600'}`}>👑 АДМИН</button>
            )}
          </div>
        </div>
        <div className="flex gap-3 items-center mr-32">
            {/* КНОПКА СПРАВКИ */}
            <button onClick={() => setShowHelp(!showHelp)} className={`p-1.5 rounded-lg border transition-all ${showHelp ? 'bg-white text-black border-white' : 'border-white/10 text-gray-500 hover:text-white hover:border-white/30'}`} title="Инструкция">
                <Icons.Help />
            </button>

            {userRole === 'knight' && (
                <button onClick={isWorking ? stopWorkShift : startWorkShift} className={`flex items-center gap-2 text-[11px] font-bold px-4 py-1.5 rounded-md transition-all ${isWorking ? 'bg-green-900/80 text-green-200 border border-green-700/50 animate-pulse shadow-[0_0_10px_rgba(0,255,0,0.2)]' : 'bg-red-900/30 text-red-300 border border-red-900/50 hover:bg-red-900/50'}`}><Icons.Clock /> {isWorking ? 'СМЕНА АКТИВНА' : 'НАЧАТЬ СМЕНУ'}</button>
            )}
            <select className="bg-[#0a0a0a]/80 border border-[#333] text-[11px] text-gray-300 rounded-md px-3 py-1.5 outline-none w-40 backdrop-blur-md focus:border-blood-red transition-colors" onChange={(e) => { const m = models.find(m => m.id === e.target.value); if(m) selectModel(m); }}><option value="">-- ВЫБОР МОДЕЛИ --</option>{models.map(m => <option key={m.id} value={m.id}>{m.name}</option>)}</select>
            <button onClick={handleLogout} className="text-[11px] font-bold text-red-900 hover:text-red-500 px-4 py-1.5 border border-red-900/30 rounded-md hover:bg-red-900/20 transition-all">ВЫХОД ({formatUsername(session.user.email)})</button>
        </div>
      </header>

      <div className="flex-1 relative flex overflow-hidden z-10">
        
        {/* АДМИН ПАНЕЛЬ */}
        {(userRole === 'king' || userRole === 'lord') && (
            <div className={`absolute inset-0 z-40 bg-black/80 backdrop-blur-sm flex items-center justify-center transition-opacity duration-300 ${activeTab === 'ADMIN' ? 'opacity-100 visible' : 'opacity-0 invisible pointer-events-none'}`}>
                <div className="w-[900px] h-[650px] glass-panel border-red-900/50 rounded-2xl p-8 shadow-[0_0_50px_rgba(163,0,0,0.3)] flex flex-col animate-slide-up">
                    <div className="flex gap-6 border-b border-white/10 pb-4 mb-6">
                        <button onClick={() => setAdminTab('USERS')} className={`text-xs font-bold tracking-wider transition-colors ${adminTab === 'USERS' ? 'text-blood-red' : 'text-gray-500 hover:text-gray-300'}`}>СОТРУДНИКИ</button>
                        <button onClick={() => setAdminTab('MODELS')} className={`text-xs font-bold tracking-wider transition-colors ${adminTab === 'MODELS' ? 'text-blood-red' : 'text-gray-500 hover:text-gray-300'}`}>МОДЕЛИ И ИИ</button>
                        <button onClick={() => { setAdminTab('STATS'); fetchStats(); }} className={`text-xs font-bold tracking-wider transition-colors ${adminTab === 'STATS' ? 'text-blood-red' : 'text-gray-500 hover:text-gray-300'}`}>СТАТИСТИКА</button>
                    </div>

                    {adminTab === 'USERS' && (
                        <div className="flex flex-col gap-6 h-full">
                             {(userRole === 'king' || userRole === 'lord') && (
                                <div className="bg-black/30 p-4 rounded-xl border border-white/5">
                                    <div className="text-[10px] text-gray-500 font-bold mb-3 uppercase tracking-wider">Создать Сотрудника</div>
                                    <div className="grid grid-cols-3 gap-4 items-end">
                                        <div className="flex flex-col gap-1"><label className="text-[10px] text-gray-500 ml-1">Логин</label><input placeholder="рыцарь" className="bg-[#0a0a0a] border border-[#333] p-2 rounded-lg text-white text-xs outline-none focus:border-blood-red" value={newUserUsername} onChange={e => setNewUserUsername(e.target.value)} /></div>
                                        <div className="flex flex-col gap-1"><label className="text-[10px] text-gray-500 ml-1">Пароль</label><input placeholder="******" className="bg-[#0a0a0a] border border-[#333] p-2 rounded-lg text-white text-xs outline-none focus:border-blood-red" value={newUserPass} onChange={e => setNewUserPass(e.target.value)} /></div>
                                        <div className="flex flex-col gap-1"><label className="text-[10px] text-gray-500 ml-1">Роль</label><select className="bg-[#0a0a0a] border border-[#333] p-2 rounded-lg text-white text-xs outline-none focus:border-blood-red" value={newUserRole} onChange={e => setNewUserRole(e.target.value)}><option value="knight">Рыцарь (Чатер)</option>{userRole === 'king' && <option value="lord">Лорд (Админ)</option>}</select></div>
                                    </div>
                                    <button onClick={createSubAccount} className="mt-4 w-full bg-gradient-to-r from-red-900 to-[#a30000] text-white font-bold py-2 rounded-lg text-xs hover:shadow-[0_0_15px_#a30000] transition-all">СОЗДАТЬ</button>
                                </div>
                             )}
                             <div className="text-center text-xs text-yellow-500 font-mono h-4">{adminMsg}</div>
                             <div className="flex-1 overflow-y-auto custom-scrollbar border-t border-white/10 pt-2">
                                {mySubordinates.map(u => (
                                    <div key={u.id} className="flex justify-between items-center p-3 hover:bg-white/5 rounded-lg text-xs text-gray-300 border-b border-white/5 transition-colors">
                                        <span className="font-bold"><span className={u.role === 'lord' ? 'text-yellow-600' : 'text-red-900'}>[{u.role === 'lord' ? 'Л' : 'Р'}]</span> {formatUsername(u.email)}</span>
                                        {(userRole === 'king' || (userRole === 'lord' && u.role !== 'lord')) && <button onClick={() => deleteUser(u.id, u.email)} className="text-red-900 hover:text-red-500 p-1"><Icons.Trash /></button>}
                                    </div>
                                ))}
                             </div>
                        </div>
                    )}

                    {adminTab === 'MODELS' && (
                        <div className="flex flex-col gap-6 h-full">
                            {userRole === 'king' && (
                                <div className="bg-black/30 p-4 rounded-xl border border-white/5 flex flex-col gap-4">
                                    <div className="text-[10px] text-gray-500 font-bold uppercase tracking-wider">Добавить Новую Модель</div>
                                    <div className="flex gap-4">
                                        <input placeholder="Имя Модели" className="flex-1 bg-[#0a0a0a] border border-[#333] p-2 rounded-lg text-white text-xs outline-none focus:border-blood-red" value={newModelName} onChange={e => setNewModelName(e.target.value)} />
                                        <select className="bg-[#0a0a0a] border border-[#333] p-2 rounded-lg text-white text-xs outline-none w-48 focus:border-blood-red" value={newModelProvider} onChange={e => setNewModelProvider(e.target.value)}>
                                            <option value="openrouter">OpenRouter</option>
                                            <option value="sambanova">SambaNova</option>
                                        </select>
                                    </div>
                                    <textarea placeholder="Характер ИИ..." className="w-full h-24 bg-[#0a0a0a] border border-[#333] p-2 rounded-lg text-white text-xs outline-none resize-none focus:border-blood-red custom-scrollbar" value={newModelPrompt} onChange={e => setNewModelPrompt(e.target.value)} />
                                    <button onClick={createModel} className="bg-gradient-to-r from-red-900 to-[#a30000] text-white px-4 py-2 rounded-lg text-xs font-bold hover:shadow-[0_0_15px_#a30000] transition-all">СОХРАНИТЬ МОДЕЛЬ</button>
                                </div>
                            )}
                            <div className="flex-1 overflow-y-auto custom-scrollbar flex flex-col gap-3">
                                {models.map(m => (
                                    <div key={m.id} className="bg-black/40 p-4 rounded-xl border border-white/5 flex flex-col gap-3 hover:border-blood-red/30 transition-colors">
                                        <div className="flex justify-between items-start">
                                            <div className="flex flex-col">
                                                <span className="text-sm font-bold text-white tracking-wide">{m.name}</span>
                                                <span className="text-[10px] text-gray-500 font-mono mt-1">ИИ: <span className="text-gold">{m.ai_provider}</span></span>
                                            </div>
                                            <div className="flex gap-3 items-center">
                                                <button onClick={() => setAssigningModelId(assigningModelId === m.id ? null : m.id)} className={`text-xs px-3 py-1.5 rounded-md font-bold transition-all flex items-center gap-2 ${assigningModelId === m.id ? 'bg-gold text-black' : 'bg-[#0a0a0a] border border-[#333] text-gray-300 hover:border-gold'}`}>
                                                    <Icons.Users /> ПЕРСОНАЛ
                                                </button>
                                                {(userRole === 'king' || userRole === 'lord') && <button onClick={() => startEditing(m)} className="text-gray-500 hover:text-white p-1 bg-white/5 rounded-md transition-colors" title="Настройки ИИ"><Icons.Settings /></button>}
                                                {userRole === 'king' && <button onClick={() => deleteModel(m.id, m.name)} className="text-red-900 hover:text-red-500 p-1 ml-2"><Icons.Trash /></button>}
                                            </div>
                                        </div>
                                        {assigningModelId === m.id && (
                                            <div className="mt-3 border-t border-white/10 pt-3 flex flex-col gap-4 animate-fade-in bg-black/20 -mx-4 -mb-4 p-4 rounded-b-xl">
                                                <div className="grid grid-cols-2 gap-4">
                                                    <div>
                                                        <div className="text-[10px] text-blood-red font-bold uppercase mb-2 tracking-wider">Рыцари (Чатеры)</div>
                                                        <div className="max-h-32 overflow-y-auto custom-scrollbar flex flex-col gap-1">
                                                            {mySubordinates.filter(u => u.role === 'knight').map(u => {
                                                                const isAssigned = (m.knight_ids || []).includes(u.id);
                                                                return (
                                                                    <div key={u.id} onClick={() => toggleAssignment(m, u.id, 'knight')} className={`cursor-pointer p-2 rounded-md text-xs flex justify-between items-center border transition-all ${isAssigned ? 'bg-blood-red/20 border-blood-red text-white' : 'bg-[#0a0a0a] border-[#333] text-gray-500 hover:border-gray-500'}`}>
                                                                        <span>{formatUsername(u.email)}</span>
                                                                        {isAssigned && <Icons.Check />}
                                                                    </div>
                                                                );
                                                            })}
                                                        </div>
                                                    </div>
                                                    {userRole === 'king' && (
                                                        <div>
                                                            <div className="text-[10px] text-gold font-bold uppercase mb-2 tracking-wider">Лорды (Админы)</div>
                                                            <div className="max-h-32 overflow-y-auto custom-scrollbar flex flex-col gap-1">
                                                                {mySubordinates.filter(u => u.role === 'lord').map(u => {
                                                                    const isAssigned = (m.lord_ids || []).includes(u.id);
                                                                    return (
                                                                        <div key={u.id} onClick={() => toggleAssignment(m, u.id, 'lord')} className={`cursor-pointer p-2 rounded-md text-xs flex justify-between items-center border transition-all ${isAssigned ? 'bg-gold/20 border-gold text-white' : 'bg-[#0a0a0a] border-[#333] text-gray-500 hover:border-gray-500'}`}>
                                                                            <span>{formatUsername(u.email)}</span>
                                                                            {isAssigned && <Icons.Check />}
                                                                        </div>
                                                                    );
                                                                })}
                                                            </div>
                                                        </div>
                                                    )}
                                                </div>
                                            </div>
                                        )}
                                        {editingModelId === m.id && (
                                            <div className="mt-3 border-t border-white/10 pt-4 flex flex-col gap-3 animate-fade-in bg-black/20 -mx-4 -mb-4 p-4 rounded-b-xl">
                                                <div className="flex justify-between items-center text-[10px] text-blood-red font-bold uppercase tracking-wider">НАСТРОЙКИ ИИ</div>
                                                <select className="bg-[#0a0a0a] border border-[#333] p-2 rounded-lg text-white text-xs outline-none focus:border-blood-red" value={editProvider} onChange={e => setEditProvider(e.target.value)}>
                                                    <option value="openrouter">OpenRouter</option>
                                                    <option value="sambanova">SambaNova</option>
                                                </select>
                                                <textarea className="w-full h-24 bg-[#0a0a0a] border border-[#333] p-2 rounded-lg text-white text-xs outline-none resize-none focus:border-blood-red custom-scrollbar font-mono" value={editPrompt} onChange={e => setEditPrompt(e.target.value)} />
                                                <div className="flex gap-3">
                                                    <button onClick={() => saveModelSettings(m.id)} className="flex-1 bg-gradient-to-r from-red-900 to-[#a30000] text-white py-2 rounded-lg text-xs font-bold hover:shadow-[0_0_15px_#a30000] transition-all">СОХРАНИТЬ</button>
                                                    <button onClick={() => setEditingModelId(null)} className="px-6 bg-[#333] text-gray-300 py-2 rounded-lg text-xs font-bold hover:bg-[#444] transition-all">ОТМЕНА</button>
                                                </div>
                                            </div>
                                        )}
                                    </div>
                                ))}
                            </div>
                        </div>
                    )}
                    
                    {adminTab === 'STATS' && (
                        <div className="flex-1 overflow-y-auto custom-scrollbar bg-black/30 rounded-xl border border-white/5 p-4">
                            <table className="w-full text-left border-collapse">
                                <thead>
                                    <tr className="text-[10px] text-gray-500 border-b border-white/10 uppercase tracking-wider">
                                        <th className="p-3 font-bold">Сотрудник</th>
                                        <th className="p-3 font-bold">Начало</th>
                                        <th className="p-3 font-bold">Конец</th>
                                        <th className="p-3 font-bold">Время</th>
                                        <th className="p-3 font-bold text-right text-gold">Прибыль</th>
                                    </tr>
                                </thead>
                                <tbody>
                                    {workLogs.map(log => (
                                        <tr key={log.id} className="text-xs text-gray-300 border-b border-white/5 hover:bg-white/5 transition-colors">
                                            <td className="p-3 font-bold text-white">{formatUsername(log.profiles?.email) || 'Unknown'}</td>
                                            <td className="p-3 font-mono text-gray-400">{new Date(log.start_time).toLocaleString('ru-RU')}</td>
                                            <td className="p-3 font-mono">{log.end_time ? new Date(log.end_time).toLocaleTimeString('ru-RU') : <span className="text-green-500 animate-pulse font-bold">АКТИВЕН</span>}</td>
                                            <td className="p-3 font-bold text-gold">{log.duration_minutes ? `${log.duration_minutes} мин` : '-'}</td>
                                            <td className={`p-3 font-bold text-right font-mono ${log.profit > 0 ? 'text-green-400' : 'text-gray-600'}`}>
                                                {log.profit ? `$${log.profit}` : '$0.00'}
                                            </td>
                                        </tr>
                                    ))}
                                </tbody>
                            </table>
                        </div>
                    )}
                </div>
            </div>
        )}

        {/* АВТОПИЛОТ (ВЕРНУЛСЯ ИЗ НЕБЫТИЯ) */}
        <div className={`absolute inset-0 z-30 bg-[#0a0a0a]/95 flex transition-opacity duration-300 ${activeTab === 'AUTO_PILOT' ? 'opacity-100 visible' : 'opacity-0 invisible pointer-events-none'}`}>
            <div className="w-80 glass-panel flex flex-col p-5 gap-5">
                <div className="flex items-center gap-2 text-white font-bold border-b border-white/10 pb-3 tracking-wide text-sm"><Icons.List /> СПИСОК ЦЕЛЕЙ</div>
                <div className="flex flex-col gap-3 flex-1">
                    <textarea className="flex-1 bg-[#121212] border border-red-900/30 rounded-xl p-3 text-xs text-gray-300 outline-none resize-none custom-scrollbar focus:border-blood-red transition-all font-mono" placeholder="Вставь ссылки или ID..." value={rawLinks} onChange={(e) => setRawLinks(e.target.value)} disabled={isAutoRunning}/>
                    <div className="flex gap-3"><button onClick={loadTargets} disabled={isAutoRunning} className="flex-1 flex items-center justify-center gap-2 bg-white/5 hover:bg-white/10 border border-white/10 text-xs text-white py-2.5 rounded-lg transition-all font-bold"><Icons.Plus /> ЗАГРУЗИТЬ {pendingCount > 0 && `(${pendingCount})`}</button><button onClick={clearTargets} disabled={isAutoRunning} className="px-4 bg-white/5 hover:text-red-500 border border-white/10 rounded-lg transition-all"><Icons.Trash /></button></div>
                </div>
                <div className="h-1/3 overflow-y-auto custom-scrollbar flex flex-col gap-1 border-t border-white/10 pt-3 bg-black/20 rounded-xl p-2">
                    {targets.length === 0 ? <div className="text-center text-xs text-gray-600 mt-4 italic">Список пуст</div> : 
                    targets.map((t, i) => (
                        <div key={i} className={`p-2 rounded-lg text-xs truncate border transition-all font-mono ${i === currentAutoIndex ? 'bg-blood-red/20 border-blood-red text-white shadow-[0_0_10px_#a30000]' : (i < resumeIndex ? 'opacity-50 text-green-500 border-transparent' : 'text-gray-400 border-transparent hover:bg-white/5')}`}>
                            <span className="opacity-50 mr-2">{i+1}.</span>{t.link.replace('https://www.manyvids.com/inbox/messages/', '')}
                        </div>
                    ))}
                </div>
            </div>

            <div className="flex-1 flex flex-col bg-[#0a0a0a] p-6 gap-6 border-r border-white/5">
                <div className="grid grid-cols-1 gap-4 bg-black/30 p-5 rounded-2xl border border-white/5">
                    <div className="text-blood-red text-xs font-bold tracking-[0.2em] uppercase mb-2">ШАБЛОНЫ СООБЩЕНИЙ (ЦИКЛ)</div>
                    {templates.map((tmpl, idx) => (
                        <div key={idx} className="flex gap-4 items-center">
                            <span className="text-gray-600 font-mono text-xs w-6 opacity-50">0{idx+1}</span>
                            <input className="flex-1 bg-[#121212] border border-red-900/30 rounded-lg p-3 text-sm text-gray-200 outline-none focus:border-blood-red transition-all" placeholder={`Шаблон сообщения #${idx+1}`} value={tmpl} onChange={(e) => { const newT = [...templates]; newT[idx] = e.target.value; setTemplates(newT); }} disabled={isAutoRunning}/>
                        </div>
                    ))}
                </div>
                <div className="flex gap-4">
                    {!isAutoRunning ? 
                        (<button onClick={startAutoPilot} disabled={targets.length === 0} className="w-full py-4 bg-gradient-to-r from-red-900 to-[#a30000] hover:from-[#a30000] hover:to-red-600 text-white font-bold rounded-xl shadow-[0_0_25px_rgba(163,0,0,0.4)] tracking-[0.2em] uppercase transition-all hover:scale-[1.02]">ЗАПУСТИТЬ АВТОПИЛОТ 🚀</button>) : 
                        (<button onClick={stopAutoPilot} className="w-full py-4 bg-gray-800 hover:bg-gray-700 text-white font-bold rounded-xl animate-pulse tracking-[0.2em] uppercase border border-red-900/50 transition-all">ЭКСТРЕННАЯ ОСТАНОВКА ⏸️</button>)
                    }
                </div>
                <div className="flex-1 bg-[#121212] rounded-2xl border border-white/5 p-4 overflow-hidden flex flex-col shadow-inner">
                    <div className="text-[10px] text-gray-500 uppercase mb-3 font-bold tracking-wider">СИСТЕМНЫЙ ЖУРНАЛ</div>
                    <div className="flex-1 overflow-y-auto custom-scrollbar font-mono text-[11px] space-y-1.5 p-2 bg-black/20 rounded-xl">
                        {autoLog.map((log, i) => <div key={i} className="text-green-500/90 border-l-2 border-green-900/50 pl-2">{log}</div>)}
                    </div>
                </div>
            </div>

            <div className="w-80 glass-panel flex flex-col p-5">
                <div className="flex items-center gap-2 text-white font-bold border-b border-white/10 pb-3 tracking-wide text-sm"><Icons.Check /> ОТЧЕТ ОТПРАВКИ</div>
                <div className="flex-1 overflow-y-auto custom-scrollbar pt-3 space-y-3">
                    {sentHistory.length === 0 ? <div className="text-center text-xs text-gray-600 mt-10 italic">Нет данных</div> : 
                    sentHistory.map((item, i) => (
                        <div key={i} className="bg-black/40 p-3 rounded-xl border border-white/5 text-xs hover:border-blood-red/30 transition-colors">
                            <div className="flex justify-between text-[10px] text-gray-500 mb-2 font-mono"><span>{item.time}</span><span className="text-blood-red font-bold uppercase">ОТПРАВЛЕНО</span></div>
                            <div className="font-bold text-gray-200 mb-2">Кому: <span className="text-white">{item.user}</span></div>
                            <div className="text-gray-400 italic truncate bg-black/20 p-2 rounded-lg">{item.template}</div>
                        </div>
                    ))}
                </div>
            </div>
        </div>

        {/* РАБОЧАЯ ЗОНА (ВЕРНУЛАСЬ ИЗ НЕБЫТИЯ) */}
        <div className={`absolute inset-0 flex z-20 bg-transparent transition-opacity duration-300 ${activeTab === 'WORK_ZONE' ? 'opacity-100 visible' : 'opacity-0 invisible pointer-events-none'}`}>
          <div className="w-80 glass-panel overflow-y-auto custom-scrollbar">
              {Array.isArray(chats) && chats.length > 0 ? chats.map(chat => {
                const showRedDot = unreadMap[chat.name] === true;
                const isPinned = pinnedMap[currentModel?.id]?.includes(chat.name);

                return (
                    <div key={chat.index} onClick={() => handleChatClick(chat)} className={`relative p-4 border-b border-white/5 flex gap-4 cursor-pointer group hover:bg-white/5 transition-all ${selectedChat?.index === chat.index ? 'bg-white/10 border-r-2 border-blood-red' : ''} ${isPinned ? 'bg-gold/5' : ''}`}>
                        <div className={`absolute top-2 right-2 p-1 rounded-full cursor-pointer z-10 transition-opacity ${isPinned ? 'opacity-100' : 'opacity-0 group-hover:opacity-100'}`} onClick={(e) => togglePin(e, chat.name)}>
                            <Icons.Pin filled={isPinned} />
                        </div>
                        <div className="relative">
                            <img src={chat.avatar || ''} className={`w-12 h-12 rounded-full bg-[#222] object-cover border-2 transition-colors ${isPinned ? 'border-gold' : 'border-white/10 group-hover:border-blood-red/50'}`} />
                            {chat.isWhale && <div className="absolute -top-1 -right-1 text-gold text-sm drop-shadow-md">👑</div>}
                            {showRedDot && <div className="absolute top-0 right-0 w-4 h-4 bg-blood-red rounded-full border-2 border-[#121212] animate-pulse shadow-[0_0_10px_#a30000]"></div>}
                        </div>
                        <div className="overflow-hidden flex-1 flex flex-col justify-center">
                            <div className="flex justify-between items-center mb-1">
                                <div className={`font-bold text-sm truncate ${selectedChat?.index === chat.index ? 'text-white' : 'text-gray-200'} group-hover:text-white transition-colors`}>{chat.name || 'Пользователь'}</div>
                                {chat.isWhale && <span className="text-[9px] bg-gold/10 text-gold px-1.5 py-0.5 rounded-md font-bold border border-gold/20">VIP</span>}
                            </div>
                            <div className={`text-xs truncate transition-colors ${showRedDot ? 'text-white font-bold' : 'text-gray-500 group-hover:text-gray-300'}`}>{chat.lastMsg || '...'}</div>
                        </div>
                    </div>
                );
              }) : (
                  <div className="flex h-full items-center justify-center text-gray-600 text-xs italic p-4 text-center">
                      {!currentModel ? "Выберите модель в меню сверху" : "Нет активных чатов"}
                  </div>
              )}
          </div>
          <div className="flex-1 flex flex-col bg-[#0a0a0a]/50 backdrop-blur-sm">
            {selectedChat ? (
  <div className="flex-1 flex flex-row w-full h-full overflow-hidden">
    
    {/* === ЛЕВАЯ КОЛОНКА (САМ ЧАТ) === */}
    <div className="flex-1 flex flex-col relative overflow-hidden">
      <div className="h-16 header-integrated flex items-center px-6 shadow-sm border-b border-white/5 flex-shrink-0">
          <img src={selectedChat.avatar || ''} className="w-10 h-10 rounded-full mr-4 border-2 border-white/10 shadow-sm object-cover" />
          <span className="font-bold text-white text-lg tracking-wide">{selectedChat.name}</span>
      </div>
                {/* 🔥 ИСПРАВЛЕНА "ГРАВИТАЦИЯ" (ОТСТУПЫ) 🔥 */}
                <div className="flex-1 overflow-y-auto p-4 flex flex-col custom-scrollbar space-y-1" ref={scrollContainerRef}>
                    {history.map((msg, idx) => { 
                        if (!msg) return null; 
                        const uniqueKey = `${idx}-${msg.time || ''}`;
                        
                        // 🔥 ФОРМАТИРОВАНИЕ ДАТЫ (День Месяц Год)
                        // 🔥 УМНОЕ ФОРМАТИРОВАНИЕ ВРЕМЕНИ (Сдвиг часового пояса)
let displayDate = msg.time || '';
if (displayDate) {
    // Ищем американское время и конвертируем
    displayDate = displayDate.replace(/(\d{1,2}):(\d{2})\s*(am|pm)/i, (match, h, m, period) => {
        let hours = parseInt(h);
        const isPM = period.toLowerCase() === 'pm';
        
        if (isPM && hours < 12) hours += 12;
        if (!isPM && hours === 12) hours = 0;

        // ⚙️ НАСТРОЙКА СДВИГА: Разница между сервером ManyVids и твоим временем.
        // Обычно это +7 часов (разница между Нью-Йорком и Восточной Европой).
        const OFFSET = 7; 
        
        let localHours = (hours + OFFSET) % 24;
        return `${localHours.toString().padStart(2, '0')}:${m}`; // Вернет красивое время, например "17:45"
    });

    // Если это свежее сообщение без даты, добавляем сегодняшнюю локальную дату
    if (!displayDate.includes(',') && !displayDate.includes('.')) {
        const now = new Date();
        displayDate = `${now.toLocaleDateString('ru-RU')} в ${displayDate}`;
    }
 }

                        return (
                            <div key={uniqueKey} className={`flex flex-col ${msg.isMine ? 'items-end' : 'items-start'} group mb-2`}>
                                {/* Дата сверху сообщения, мелким шрифтом */}
                                {msg.time && <div className="text-[10px] text-gray-500 px-2 font-mono opacity-70 mb-0.5">{displayDate}</div>}
                                
                                <div className={`flex ${msg.isMine ? 'flex-row-reverse' : 'flex-row'} items-end max-w-[85%]`}>
                                    
                                    {/* Аватарка (Слева) */}
                                    {!msg.isMine && (
                                        <div className="w-8 h-8 rounded-full mr-2 overflow-hidden flex-shrink-0 border border-white/10 bg-[#222]">
                                            <img src={msg.avatar || selectedChat?.avatar || ''} className="w-full h-full object-cover" onError={(e) => e.target.style.display = 'none'} />
                                        </div>
                                    )}

                                    {/* Тело сообщения */}
                                    <div className={`relative px-4 py-2 rounded-2xl text-sm shadow-md backdrop-blur-sm ${msg.isMine ? 'bg-gradient-to-r from-red-900 to-[#a30000] text-white rounded-tr-sm' : 'bg-[#1a1a1a] text-gray-200 border border-white/5 rounded-tl-sm'}`}>
                                        <span className="whitespace-pre-wrap leading-snug block break-words" dangerouslySetInnerHTML={{ __html: msg.html || '' }} />
                                        
                                        {!msg.isMine && !msg.translated && (
                                            <button onClick={() => translateMessage(idx, msg.html)} className="absolute -right-6 bottom-0 text-gray-500 hover:text-blood-red opacity-0 group-hover:opacity-100 transition-all scale-90 hover:scale-110" title="Перевести">
                                                <Icons.Translate />
                                            </button>
                                        )}
                                    </div>
                                </div>
                            </div>
                        ); 
                    })}
                    <div ref={messagesEndRef} />
                </div>
                {/* Исправленный блок ввода текста (удален header-integrated) */}
                <div className="p-4 bg-[#0a0a0a] border-t border-white/5 flex gap-3 items-end">
                    <div className="flex gap-2 pb-1 relative">
    <button 
        onClick={() => setShowEmojiPicker(!showEmojiPicker)} 
        className={`text-gray-400 hover:text-white p-2.5 rounded-xl transition-all ${showEmojiPicker ? 'bg-white/10 text-white shadow-[0_0_10px_rgba(255,255,255,0.1)]' : 'bg-white/5 hover:bg-white/10'}`} 
        title="Смайлики"
    >
        <Icons.Emoji />
    </button>

    {/* 🔥 НАША НОВАЯ ПАНЕЛЬ ЭМОДЗИ 🔥 */}
    {showEmojiPicker && (
        <div className="absolute bottom-[120%] left-0 bg-[#121212] border border-red-900/30 p-3 rounded-xl shadow-[0_0_20px_rgba(0,0,0,0.8)] flex gap-2 flex-wrap w-[280px] z-50 animate-fade-in backdrop-blur-xl">
            {QUICK_EMOJIS.map(emoji => (
                <button 
                    key={emoji} 
                    onClick={() => { setInputText(prev => prev + emoji); setShowEmojiPicker(false); }}
                    className="text-xl hover:bg-white/10 p-1.5 rounded-lg transition-transform hover:scale-125"
                >
                    {emoji}
                </button>
            ))}
        </div>
    )}
    </div>
                    <div className="flex-1 flex flex-col bg-[#121212]/80 border border-red-900/30 rounded-2xl focus-within:border-blood-red focus-within:shadow-[0_0_20px_rgba(163,0,0,0.2)] transition-all backdrop-blur-md">
                        <div className="px-3 pt-1.5 flex justify-between border-b border-white/5 items-center">
                            <div className="flex gap-2">
                                 <button onClick={generateAiReply} className={`p-1.5 rounded-lg transition-all ${aiLoading ? 'text-gold animate-spin' : 'text-gray-500 hover:text-blood-red scale-90 hover:scale-110'}`} disabled={aiLoading} title="Ответ ИИ"><Icons.Robot /></button>
                            </div>
                            <label className="flex items-center gap-1.5 text-[10px] text-gray-500 cursor-pointer hover:text-gray-300 py-1 font-bold uppercase tracking-wider transition-colors"><input type="checkbox" checked={isTranslateOn} onChange={() => setIsTranslateOn(!isTranslateOn)} className="accent-blood-red" /> ПЕРЕВОД (EN → RU)</label>
                        </div>
                        <div className="flex gap-3 items-end p-3">
                            <textarea className="flex-1 bg-transparent border-none text-white text-sm p-1 outline-none resize-none max-h-32 min-h-[40px] placeholder-gray-600 custom-scrollbar" value={inputText} onChange={e => setInputText(e.target.value)} onKeyDown={handleKeyDown} placeholder="Напиши приказ..." rows={1}/>
                            <button onClick={sendMessage} className={`p-2.5 rounded-xl transition-all ${inputText.trim() ? 'bg-blood-red text-white hover:bg-red-700 shadow-lg hover:shadow-[0_0_15px_#a30000] hover:scale-105' : 'bg-white/5 text-gray-600'}`}><Icons.Send /></button>
                        </div>
                    </div>
            </div>
        </div>

        {/* === ПРАВАЯ КОЛОНКА (ДОСЬЕ) === */}
        <div className="w-72 bg-[#0d0d0d] border-l border-white/5 flex flex-col flex-shrink-0 z-10 shadow-[-5px_0_15px_rgba(0,0,0,0.5)]">
            <div className="h-16 flex items-center px-4 border-b border-white/5 bg-[#121212] gap-2 text-blood-red font-bold tracking-widest text-xs uppercase flex-shrink-0">
                <Icons.List /> ДОСЬЕ КЛИЕНТА
            </div>
            <div className="flex-1 p-4 flex flex-col gap-2">
                <div className="flex justify-between items-center mb-1">
                    <span className="text-[10px] text-gray-500 uppercase tracking-wider">Секретные материалы</span>
                    {notesMap[`${currentModel?.id}_${selectedChat.name}`] && <span className="text-[9px] text-blood-red uppercase animate-pulse">Сохранено</span>}
                </div>
                <ClientNotes 
                    currentModel={currentModel} 
                    selectedChat={selectedChat} 
                    notesMap={notesMap} 
                    setNotesMap={setNotesMap} 
                  />
            </div>
        </div>

     </div>
         ) : (
                // 🔥 ПУСТОЙ ЭКРАН С ПОДСКАЗКОЙ
                <div className="flex-1 flex flex-col items-center justify-center text-gray-600 gap-4 opacity-50 relative group cursor-pointer" onClick={() => setShowHelp(true)}>
                    <Icons.Emblem />
                    <div className="text-xl tracking-[0.2em] font-bold">ЗАЩИЩЕННЫЙ КАНАЛ CAMELOT</div>
                    <div className="text-sm border border-gray-700 px-4 py-2 rounded-full hover:bg-white/5 transition-colors flex items-center gap-2">
                        <Icons.Help /> Нажмите для инструкций
                    </div>
                </div>
            )}
          </div>
        </div>

        {/* ШЛЮЗ БРАУЗЕРА */}
        <div className={`absolute inset-0 z-10 bg-[#0a0a0a] flex items-center justify-center transition-opacity duration-500 ${activeTab === 'BACKSTAGE' ? 'opacity-100 visible' : 'opacity-0 invisible pointer-events-none'}`}>
            {!isBrowserActive ? (
                <div className="flex flex-col items-center gap-8 animate-fade-in scale-95 hover:scale-100 transition-all duration-500">
                    <button 
                        onClick={forceEnterBrowser} 
                        disabled={userRole !== 'king' && (!currentModel || (!isWorking && userRole === 'knight'))}
                        className={`group relative p-8 rounded-full bg-gradient-to-br from-[#1a1a1a] to-black border-4 ${(userRole !== 'king' && (!currentModel || (!isWorking && userRole === 'knight'))) ? 'border-gray-800 opacity-50 cursor-not-allowed' : 'border-blood-red cursor-pointer hover:border-gold shadow-[0_0_30px_rgba(163,0,0,0.3)] hover:shadow-[0_0_60px_rgba(212,175,55,0.5)]'} transition-all duration-500`}
                    >
                        <div className={`text-blood-red ${(userRole === 'king' || currentModel) ? 'group-hover:text-gold scale-110 group-hover:scale-125 transition-all duration-500' : 'text-gray-700'}`}>
                            <Icons.Lock />
                        </div>
                        {currentModel && <div className="absolute inset-0 rounded-full bg-blood-red/10 group-hover:bg-gold/10 animate-pulse"></div>}
                    </button>
                    
                    <div className="text-center">
                        <div className="text-white font-bold tracking-[0.3em] text-2xl mb-2 uppercase drop-shadow-md">
                            {currentModel ? `ТЕРМИНАЛ: ${currentModel.name.toUpperCase()}` : (userRole === 'king' ? 'МАСТЕР ДОСТУП' : 'ТЕРМИНАЛ ЗАБЛОКИРОВАН')}
                        </div>
                        <div className="text-gray-500 text-xs font-mono tracking-wider mb-4">
                            {userRole === 'king' ? 'ВХОД РАЗРЕШЕН' : (!currentModel ? 'ВЫБЕРИТЕ ПРОТОКОЛ МОДЕЛИ' : (!isWorking && userRole === 'knight' ? '⚠️ ТРЕБУЕТСЯ СМЕНА ДЛЯ ВХОДА' : 'НАЖМИТЕ НА ЗАМОК ДЛЯ ВХОДА'))}
                        </div>
                        {/* ПОДСКАЗКА В БРАУЗЕРЕ */}
                        <div className="text-[10px] text-gray-600 border border-gray-800 rounded-full px-3 py-1 inline-flex items-center gap-2 cursor-pointer hover:text-white transition-colors" onClick={() => setShowHelp(true)}>
                            <Icons.Help /> Инструкция по браузеру
                        </div>
                    </div>
                </div>
            ) : (
                <div className="flex flex-col w-full h-full animate-fade-in">
                    {userRole === 'king' ? (
                        <div className="h-10 header-integrated flex items-center px-4 justify-between select-none">
                            <div className="text-xs text-gray-400 flex items-center gap-2 font-mono tracking-wider">
                                <span className="w-2 h-2 rounded-full bg-green-500 animate-pulse shadow-[0_0_5px_#0f0]"></span> ОНЛАЙН: <span className="text-white font-bold">{currentModel?.name || 'MASTER'}</span>
                            </div>
                            <button onClick={updateSession} className="bg-gradient-to-r from-red-900 to-[#a30000] hover:from-[#a30000] hover:to-red-600 text-white px-4 py-1.5 rounded-lg text-[10px] font-bold flex items-center gap-2 transition-all shadow-md hover:shadow-[0_0_15px_#a30000] tracking-wider uppercase">
                                <Icons.Save /> СОХРАНИТЬ СЕССИЮ
                            </button>
                        </div>
                    ) : (
                        <div className="h-1.5 bg-gradient-to-r from-red-900 via-[#a30000] to-red-900 w-full relative group shadow-[0_0_10px_#a30000]">
                            <div className="absolute top-full right-2 mt-1 bg-black/90 text-[9px] text-red-300 px-2 py-1 rounded-md border border-red-900/50 opacity-0 group-hover:opacity-100 transition-opacity pointer-events-none uppercase font-bold tracking-wider backdrop-blur-md">РЕЖИМ НАБЛЮДЕНИЯ (ТОЛЬКО ЧТЕНИЕ)</div>
                        </div>
                    )}
                    <webview ref={webviewRef} src="about:blank" partition="persist:camelot" allowpopups="true" className="w-full h-full" />
                </div>
            )}
        </div>
      </div>
    </div>
  );
}

export default App;