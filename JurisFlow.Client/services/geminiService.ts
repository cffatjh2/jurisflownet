import { GoogleGenAI } from "@google/genai";
import { AIRequest } from "../types";
import { api } from "./api";

const apiKey = (typeof import.meta !== 'undefined' && (import.meta as any).env?.VITE_GEMINI_API_KEY) || process.env.API_KEY || '';

// Only initialize if API key is available
const ai = apiKey ? new GoogleGenAI({ apiKey }) : null;

// Legacy function for direct drafting (kept for backward compatibility if needed, though UI is changing)
export const generateLegalDraft = async (request: AIRequest): Promise<string> => {
  if (!ai) {
    const fallback = await api.ai.chat({
      message: request.prompt,
      contextData: request.context || '',
      enableSearch: false
    }).catch(() => null);
    return fallback?.text || "AI service is not available right now.";
  }
  try {
    const model = "gemini-2.5-flash";
    const systemInstruction = `You are an expert US Legal Assistant. Tone: ${request.tone}. Context: ${request.context || 'None'}. Draft the requested document.`;

    const response = await ai.models.generateContent({
      model: model,
      contents: request.prompt,
      config: {
        systemInstruction: systemInstruction,
        temperature: 0.3,
      }
    });

    return response.text || "Unable to generate draft.";
  } catch (error) {
    console.error("Gemini API Error:", error);
    return "Error communicating with AI.";
  }
};

interface ChatMessage {
  role: 'user' | 'model';
  parts: { text: string }[];
}

export const createLegalChatSession = async (
  history: ChatMessage[],
  lastMessage: string,
  contextData: string,
  enableSearch: boolean = false
): Promise<{ text: string, sources?: any[] }> => {
  if (!ai) {
    const fallback = await api.ai.chat({
      history,
      message: lastMessage,
      contextData,
      enableSearch
    }).catch(() => null);
    return fallback || { text: "AI service is not available right now." };
  }
  try {
    // 1. Configure Tools (Google Search for research)
    const tools = enableSearch ? [{ googleSearch: {} }] : [];

    // 2. Build System Instruction
    const systemInstruction = `You are 'Juris', a highly intelligent AI Legal Associate for a US Law Firm.
    
    CAPABILITIES:
    - Summarize legal documents (Depositions, Contracts, Medical Records).
    - Draft legal correspondence, motions, and memos.
    - Conduct basic legal research using Google Search (when enabled) to find case law and statutes.
    - Analyze case strategy based on provided context.

    RULES:
    - Be professional, precise, and concise.
    - If drafting, use standard legal formatting.
    - If researching, CITE YOUR SOURCES.
    - Context provided from uploaded documents: ${contextData || "No specific documents linked."}
    `;

    // 3. Initialize Chat
    // Note: We are using a stateless approach for the service wrapper, recreating the chat state each time 
    // to keep the frontend simple, or we could use the SDK's chat history management. 
    // For this implementation, we use generateContent with the full history as the prompt context 
    // OR use the proper chat method. Let's use ai.chats.create for best practice.

    const chat = ai.chats.create({
      model: enableSearch ? "gemini-2.5-flash" : "gemini-2.5-flash", // Use standard model, search tool handles grounding
      config: {
        systemInstruction: systemInstruction,
        tools: tools,
        temperature: 0.4,
      },
      history: history // Pass previous conversation
    });

    // 4. Send Message
    const result = await chat.sendMessage({ message: lastMessage });

    // 5. Extract Text and Grounding Metadata
    const text = result.text;
    const groundingChunks = result.candidates?.[0]?.groundingMetadata?.groundingChunks;

    // Extract sources if search was used
    let sources: any[] = [];
    if (groundingChunks) {
      sources = groundingChunks
        .map((chunk: any) => chunk.web ? { title: chunk.web.title, uri: chunk.web.uri } : null)
        .filter((s: any) => s !== null);
    }

    return { text: text || "I couldn't generate a response.", sources };

  } catch (error) {
    console.error("Legal Chat Error:", error);
    return { text: "I encountered an error processing your legal request. Please try again." };
  }
};
