const { app, BrowserWindow, globalShortcut, ipcMain } = require('electron');
const path = require('path');
const fs = require('fs');
const Database = require('better-sqlite3');

// Single-instance lock – prevent multiple copies running on the same tablet
const gotLock = app.requestSingleInstanceLock();
if (!gotLock) { app.quit(); }

/* ================================
   DATABASE & CSV SETUP
=================================*/
const sharedDir = path.join(
  process.env.PUBLIC || path.join('C:', 'Users', 'Public', 'Documents'),
  'AnrcQuiz'
);
fs.mkdirSync(sharedDir, { recursive: true });

const dbPath = path.join(sharedDir, 'quiz.db');
const csvPath = path.join(sharedDir, 'quiz.csv');

const db = new Database(dbPath);
db.pragma('journal_mode = WAL');
db.exec(`
  CREATE TABLE IF NOT EXISTS responses (
    id        INTEGER PRIMARY KEY AUTOINCREMENT,
    timestamp TEXT,
    question1 TEXT,
    question2 TEXT,
    question3 TEXT,
    path      TEXT
  )
`);

const insertStmt = db.prepare(`
  INSERT INTO responses (timestamp, question1, question2, question3, path)
  VALUES (@timestamp, @question1, @question2, @question3, @path)
`);

function csvEscape(s) {
  if (!s) return '""';
  return '"' + String(s).replace(/"/g, '""') + '"';
}

function appendCsvRow(row) {
  const needsHeader = !fs.existsSync(csvPath) || fs.statSync(csvPath).size === 0;
  let line = '';
  if (needsHeader) {
    line += 'Timestamp,Question 1,Question 2,Question 3,Path\n';
  }
  line += [
    csvEscape(row.timestamp),
    csvEscape(row.question1),
    csvEscape(row.question2),
    csvEscape(row.question3),
    csvEscape(row.path)
  ].join(',') + '\n';
  fs.appendFileSync(csvPath, line, 'utf8');
}

/* ================================
   IPC HANDLER
=================================*/
ipcMain.handle('save-response', (_event, data) => {
  const row = {
    timestamp: new Date().toISOString(),
    question1: data.question1 || '',
    question2: data.question2 || '',
    question3: data.question3 || '',
    path:      data.path || ''
  };
  insertStmt.run(row);
  appendCsvRow(row);
  return { ok: true };
});

/* ================================
   WINDOW
=================================*/
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
      nodeIntegration: false,
      preload: path.join(__dirname, 'preload.js')
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
  db.close();
});
