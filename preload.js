const { contextBridge, ipcRenderer } = require('electron');

contextBridge.exposeInMainWorld('quizAPI', {
  saveResponse: (data) => ipcRenderer.invoke('save-response', data)
});
