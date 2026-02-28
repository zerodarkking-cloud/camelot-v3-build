export interface IElectronAPI {
  loadSessionCookies: (cookies: any[], partitionName: string) => Promise<boolean>;
  saveSessionCookies: (partitionName: string) => Promise<any[]>;
  translate: (data: { text: string; lang: string }) => Promise<string>;
  askAi: (data: { history: any[]; prompt: string; provider: string }) => Promise<string>;
  logoutSession: (partitionName: string) => Promise<boolean>;
}

declare global {
  interface Window {
    electronAPI: IElectronAPI;
  }
}