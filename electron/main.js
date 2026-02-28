import { app, BrowserWindow, session, ipcMain, net } from 'electron';
import path from 'path';
import { fileURLToPath } from 'url';
import isDev from 'electron-is-dev';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const PRELOAD_PATH = path.join(__dirname, 'preload.cjs'); 
const DONOR_USER_AGENT = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36';

// 🔥 МАГИЯ: Самый надежный способ получения пути к иконке в Electron
const iconPath = path.join(app.getAppPath(), isDev ? 'public/icon.ico' : 'dist/icon.ico');

// Глобальный перехватчик для всех новых сессий (изоляция моделей)
app.on('session-created', (customSession) => {
    customSession.webRequest.onBeforeSendHeaders({ urls: ['*://*.manyvids.com/*'] }, (details, cb) => {
        details.requestHeaders['User-Agent'] = DONOR_USER_AGENT;
        cb({ cancel: false, requestHeaders: details.requestHeaders });
    });
    
    customSession.webRequest.onHeadersReceived({ urls: ['*://*.manyvids.com/*'] }, (details, cb) => {
        const responseHeaders = details.responseHeaders || {};
        delete responseHeaders['x-frame-options'];
        delete responseHeaders['content-security-policy'];
        cb({ cancel: false, responseHeaders });
    });
});

function createWindow() {
  const mainWindow = new BrowserWindow({
    width: 1400,
    height: 900,
    backgroundColor: '#353535', // Anthracite фон Camelot
    title: 'Camelot Browser',
    icon: iconPath, 
    autoHideMenuBar: true, 
    
    titleBarStyle: 'hidden', 
    titleBarOverlay: {
        color: '#0a0a0a',        // Идеально сольется с основным фоном шапки
        symbolColor: '#a30000',  // Ярко-белые значки (или Blood Red в зависимости от задумки)
        height: 35 
    },

    webPreferences: {
      nodeIntegration: true,
      contextIsolation: false,
      webviewTag: true,
      webSecurity: false
    }
  });

  // --- IPC Handlers ---

  ipcMain.handle('save-session-cookies', async (event, partitionName = 'persist:camelot') => {
      try { 
          const targetSession = session.fromPartition(partitionName);
          const cookies = await targetSession.cookies.get({ domain: 'manyvids.com' }); 
          return cookies; 
      } catch (e) { return []; }
  });

  ipcMain.handle('load-session-cookies', async (event, cookies, partitionName = 'persist:camelot') => {
      try {
        const targetSession = session.fromPartition(partitionName);
        await targetSession.clearStorageData({ storages: ['cookies', 'localstorage', 'indexdb', 'shadercache', 'serviceworkers', 'cachestorage'] });
        if (!cookies || cookies.length === 0) return false;
        
        for (const cookie of cookies) {
            const scheme = cookie.secure ? 'https' : 'http';
            let domain = cookie.domain;
            const cleanDomain = domain.startsWith('.') ? domain.substring(1) : domain;
            const url = `${scheme}://${cleanDomain}${cookie.path}`;
            await targetSession.cookies.set({ url: url, name: cookie.name, value: cookie.value, domain: cookie.domain, path: cookie.path, secure: cookie.secure, httpOnly: cookie.httpOnly, expirationDate: cookie.expirationDate, sameSite: cookie.sameSite });
        }
        return true;
      } catch (e) { return false; }
  });

  ipcMain.handle('translate', async (event, { text, lang }) => {
      try { if (!text) return ""; const url = `https://api.mymemory.translated.net/get?q=${encodeURIComponent(text)}&langpair=${lang}`; const response = await net.fetch(url); const json = await response.json(); return json.responseData.translatedText; } catch (error) { return text; }
  });

  ipcMain.handle('ask-ai', async (event, { history, prompt, provider }) => {
      console.log(`🧠 AI Request via: ${provider}`);
      const conversation = history.slice(-10).map(msg => ({ role: msg.isMine ? "assistant" : "user", content: msg.rawText || msg.html.replace(/<[^>]*>?/gm, '') }));
      const messages = [{ role: "system", content: `${prompt}\n\nIMPORTANT: You are a real person, not an AI. Never mention being an AI. Keep answers short (1-3 sentences). Flirt and sell.` }, ...conversation];
      try {
          if (provider === 'openrouter') {
              const OPENROUTER_KEY = "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"; 
              const modelsToTry = ["meta-llama/llama-3.1-8b-instruct:free", "google/gemini-2.0-flash-exp:free", "google/gemma-2-9b-it:free", "mistralai/mistral-7b-instruct:free", "microsoft/phi-3-medium-128k-instruct:free"];
              for (const model of modelsToTry) {
                  try {
                      console.log(`Trying model: ${model}...`);
                      const response = await net.fetch("https://openrouter.ai/api/v1/chat/completions", { method: "POST", headers: { "Authorization": `Bearer ${OPENROUTER_KEY}`, "Content-Type": "application/json", "HTTP-Referer": "https://camelot.team", "X-Title": "Camelot Browser" }, body: JSON.stringify({ model: model, messages: messages, temperature: 0.85, max_tokens: 200 }) });
                      const rawText = await response.text(); const json = JSON.parse(rawText);
                      if (!json.error && json.choices && json.choices.length > 0) { let reply = json.choices[0].message.content.trim(); return reply.replace(/^["']|["']$/g, ''); }
                      console.log(`Model ${model} failed/busy.`);
                  } catch (err) { continue; }
              }
              return "⚠️ Все каналы OpenRouter перегружены. Переключись на SambaNova в настройках.";
          } else if (provider === 'sambanova') {
              const SAMBANOVA_KEY = "e6a2b786-f47c-4db1-9c76-7fd906e429d4";
              const response = await net.fetch("https://api.sambanova.ai/v1/chat/completions", { method: "POST", headers: { "Authorization": `Bearer ${SAMBANOVA_KEY}`, "Content-Type": "application/json" }, body: JSON.stringify({ model: "Meta-Llama-3.1-8B-Instruct", messages: messages, max_tokens: 150, temperature: 0.7 }) });
              const rawText = await response.text();
              try { const json = JSON.parse(rawText); if (json.error) return `SambaNova Error: ${json.error.message}`; return json.choices[0].message.content.trim(); } catch (e) { return `SambaNova Fail: ${rawText.substring(0, 50)}...`; }
          } else {
              const GEMINI_KEY = "AIzaSyA7AevLtsQKFklXVa1JvFrUW7honfPX20E"; const geminiHistory = history.slice(-6).map(msg => `${msg.isMine ? 'Model' : 'User'}: ${msg.rawText || msg.html.replace(/<[^>]*>?/gm, '')}`).join('\n');
              const url = `https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-flash:generateContent?key=${GEMINI_KEY}`;
              const response = await net.fetch(url, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ contents: [{ parts: [{ text: `${prompt}\n\nCONTEXT:\n${geminiHistory}\n\nReply (short):` }] }] }) });
              const json = await response.json(); if (json.candidates) return json.candidates[0].content.parts[0].text.trim(); return "Gemini Silent";
          }
      } catch (error) { return "AI Network Error"; }
  });

  ipcMain.handle('logout-session', async (event, partitionName = 'persist:camelot') => { 
      const targetSession = session.fromPartition(partitionName); 
      await targetSession.clearStorageData(); 
      return true; 
  });
  
  ipcMain.on('spy-data', (event, data) => { if(!mainWindow.isDestroyed()) mainWindow.webContents.send('ui-update', data); });
  
  // --- Настройки WebContents ---

  mainWindow.webContents.on('will-attach-webview', (event, webPreferences) => { 
      webPreferences.preload = PRELOAD_PATH; 
      webPreferences.nodeIntegration = false; 
      webPreferences.contextIsolation = true; 
      delete webPreferences.preloadURL; 
  });
  
  mainWindow.webContents.setWindowOpenHandler(({ url }) => {
      return { 
          action: 'allow', 
          overrideBrowserWindowOptions: { 
              autoHideMenuBar: true, 
              webPreferences: { 
                  preload: PRELOAD_PATH, 
                  contextIsolation: true, 
                  webSecurity: false 
              } 
          } 
      };
  });
  
  mainWindow.webContents.setUserAgent(DONOR_USER_AGENT);
  
  // --- Логика Загрузки (Dev vs Prod) ---

  if (isDev) { 
      const devUrl = 'http://127.0.0.1:5173';
      mainWindow.loadURL(devUrl).catch(() => setTimeout(() => mainWindow.loadURL(devUrl), 1000)); 
  } else { 
      const indexPath = path.join(__dirname, '../dist/index.html'); 
      
      // Панель разработчика отключена для финальной сборки по умолчанию
      
      mainWindow.loadFile(indexPath).catch(err => {
          console.error("Критическая ошибка загрузки index.html:", err);
      }); 
  }

  // 🔥 Секретная комбинация Ctrl+Shift+I для вызова DevTools
  mainWindow.webContents.on('before-input-event', (event, input) => {
      if (input.control && input.shift && input.key.toLowerCase() === 'i') {
          mainWindow.webContents.toggleDevTools();
          event.preventDefault();
      }
  });
}

app.whenReady().then(createWindow);
app.on('window-all-closed', () => { if (process.platform !== 'darwin') app.quit(); });