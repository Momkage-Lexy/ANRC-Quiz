const { app, BrowserWindow, globalShortcut, dialog } = require('electron');
const path = require('path');

// Single-instance lock – prevent multiple copies running on the same tablet
const gotLock = app.requestSingleInstanceLock();
if (!gotLock) { app.quit(); }

let win;

function createWindow() {
  win = new BrowserWindow({
    show: false,
    fullscreen: true,
    kiosk: true,
    frame: false,
    autoHideMenuBar: true,
    backgroundColor: '#d73f09',
    webPreferences: {
      contextIsolation: true,
      nodeIntegration: false
    }
  });

  win.loadFile('index.html');

  // Ensure fullscreen is active once the page is ready (some Windows
  // tablets ignore the constructor flag until the window is shown)
  win.once('ready-to-show', () => {
    win.setFullScreen(true);
    win.setKiosk(true);
    win.show();
  });

  // Block navigation away from the quiz
  win.webContents.on('will-navigate', (e) => e.preventDefault());
  win.webContents.setWindowOpenHandler(() => ({ action: 'deny' }));

  // Admin exit: Ctrl+Shift+Q (so regular users can't accidentally close)
  globalShortcut.register('CommandOrControl+Shift+Q', () => {
    app.quit();
  });
}

app.whenReady().then(createWindow);

// Re-focus if user tries to open a second instance
app.on('second-instance', () => {
  if (win) {
    if (win.isMinimized()) win.restore();
    win.focus();
  }
});

app.on('window-all-closed', () => app.quit());

app.on('will-quit', () => {
  globalShortcut.unregisterAll();
});
