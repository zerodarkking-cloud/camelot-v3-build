const { ipcRenderer } = require('electron');

const cleanText = (text) => text?.replace(/\s+/g, ' ').trim() || '';

const scrapeAndSend = () => {
    try {
        const isLogin = !!document.querySelector('input[type="password"]');
        const inputField = document.querySelector('textarea[placeholder*="Message"]') || 
                           document.querySelector('[contenteditable="true"]');
        const isChatAvailable = !!inputField && !inputField.disabled;

        // --- 1. ЧАТЫ (СЛЕВА) ---
        const chats = [];
        const chatItems = document.querySelectorAll('[class*="PressableConversation-module-scss-module__u2jsia__pressable-container"]');
        
        chatItems.forEach((item, index) => {
            const nameEl = item.querySelector('[class*="text-display-name"]');
            const imgEl = item.querySelector('img');
            const msgEl = item.querySelector('[class*="text-preview-last-message"]');
            const vipIcon = item.querySelector('svg[data-name="premium-icon"]');
            const isUnreadSite = item.innerHTML.includes('unread'); // Простая проверка на не прочитанное

            if (nameEl && imgEl) {
                chats.push({
                    index: index,
                    name: cleanText(nameEl.innerText),
                    avatar: imgEl.src,
                    lastMsg: cleanText(msgEl?.innerText) || '...',
                    isWhale: !!vipIcon,
                    isUnreadSite: isUnreadSite
                });
                item.setAttribute('data-camelot-index', index);
            }
        });

        // --- 2. ИСТОРИЯ (ЦЕНТР) ---
        // Ищем все блоки сообщений
        let messageNodes = Array.from(document.querySelectorAll('[class*="container-messages"]'));

        // 🔥 1. СОРТИРОВКА (Исправляет "Вверх ногами")
        // Сортируем элементы строго по их положению на экране (Y)
        messageNodes.sort((a, b) => {
            return a.getBoundingClientRect().top - b.getBoundingClientRect().top;
        });

        const msgs = messageNodes.map(node => {
            const textEl = node.querySelector('[class*="text-message"]');
            const mediaImg = node.querySelector('[class*="images-container"] img');
            
            // 🔥 2. ОПРЕДЕЛЕНИЕ СВОЙ/ЧУЖОЙ (Классический метод)
            // Ищем слово 'receiver' в классе. Если оно есть - это КЛИЕНТ. Если нет - это Я.
            const className = (node.className || '').toLowerCase();
            const isReceiver = className.includes('receiver');
            const isMine = !isReceiver; 

            // Время
            let time = '';
            const parentRow = node.closest('[class*="container-dialogue"]');
            if (parentRow) {
                const timeHeader = parentRow.querySelector('[class*="timestamp"]');
                if(timeHeader) time = timeHeader.innerText;
            }

            let contentHTML = "";
            if (mediaImg) {
                contentHTML += `<img src="${mediaImg.src}" style="max-width: 200px; border-radius: 8px; margin-bottom: 5px;">`;
            }
            if (textEl) {
                contentHTML += textEl.innerHTML;
            }

            // 🔥 3. ФИЛЬТР ПУСТОТЫ (Исправляет "Гравитацию")
            if (!contentHTML || contentHTML.trim() === '') return null;

            return { 
                html: contentHTML, 
                isMine: isMine, 
                time: time 
            };
        }).filter(m => m !== null); // Убираем null

        const lastMessageIsMine = msgs.length > 0 ? msgs[msgs.length - 1].isMine : true;

        ipcRenderer.send('spy-data', {
            status: isLogin ? 'LOGIN_NEEDED' : 'ONLINE',
            chats: chats,
            history: msgs,
            lastMessageIsMine: lastMessageIsMine,
            isChatAvailable: isChatAvailable
        });

    } catch (e) {}
};

setInterval(scrapeAndSend, 1000);

// --- ОБРАБОТЧИКИ ---
ipcRenderer.on('click-chat', (event, index) => {
    const target = document.querySelector(`[data-camelot-index="${index}"]`);
    if (target) target.click();
});

ipcRenderer.on('send-message', (event, text) => {
    const input = document.querySelector('[contenteditable="true"]');
    if (input) {
        input.focus();
        document.execCommand('selectAll', false, null);
        document.execCommand('insertText', false, text);
        input.dispatchEvent(new Event('input', { bubbles: true }));
        
        setTimeout(() => {
            // Ищем кнопку отправки разными способами
            const btns = Array.from(document.querySelectorAll('button'));
            const sendBtn = document.querySelector('button[class*="button-send-message"]') ||
                            btns.find(b => b.innerText.toUpperCase() === 'SEND' || b.querySelector('svg'));
            if (sendBtn && !sendBtn.disabled) sendBtn.click();
        }, 100);
    }
});

ipcRenderer.on('trigger-action', (event, action) => {
    if (action === 'emoji') {
        // Ищем оригинальную иконку
        const emojiIcon = document.querySelector('svg[data-name="Emoji Icon"]');
        let target = document.querySelector('button[aria-label*="emoji" i]') || document.querySelector('.emoji-button');

        if (!target && emojiIcon) {
            // Берем родительский элемент (саму кнопку), так как клик по голой SVG React часто игнорирует
            target = emojiIcon.closest('button') || emojiIcon.closest('[role="button"]') || emojiIcon.parentElement;
        }

        if (target) {
            // Эмуляция 100% реального клика мышки для обхода защиты React
            const mouseEventInit = { bubbles: true, cancelable: true, view: window };
            target.dispatchEvent(new MouseEvent('mousedown', mouseEventInit));
            target.dispatchEvent(new MouseEvent('mouseup', mouseEventInit));
            target.dispatchEvent(new MouseEvent('click', mouseEventInit));
        }
        return; 
    }

    // Логика для загрузки файлов
    const inputs = Array.from(document.querySelectorAll('input[type="file"]'));
    let targetFile = null;
    if (action === 'image') targetFile = inputs.find(i => i.accept && i.accept.includes('image'));
    if (action === 'video') targetFile = inputs.find(i => i.accept && i.accept.includes('video'));
    if (targetFile) targetFile.click();
});