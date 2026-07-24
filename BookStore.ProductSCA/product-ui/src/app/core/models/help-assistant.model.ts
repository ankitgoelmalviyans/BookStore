export interface HelpAssistantMessage {
  role: 'user' | 'assistant';
  content: string;
}

export interface HelpAssistantAskResponse {
  answer: string;
}
