/**
 * useConversationManagement - Hook for managing conversation CRUD operations
 */

import { useState, useCallback } from "react";
import { apiClient } from "@/services/api";
import { useDevUIStore } from "@/stores";
import { clearStreamingState } from "@/services/streaming-state";
import type { AgentInfo, ExtendedResponseStreamEvent } from "@/types";

type DebugEventHandler = (event: ExtendedResponseStreamEvent | "clear") => void;

export interface ConversationError {
  message: string;
  code?: string;
  type?: string;
}

export interface UseConversationManagementOptions {
  selectedAgent: AgentInfo;
  onDebugEvent: DebugEventHandler;
}

export interface UseConversationManagementReturn {
  conversationError: ConversationError | null;
  setConversationError: React.Dispatch<React.SetStateAction<ConversationError | null>>;
  handleNewConversation: () => Promise<void>;
  handleDeleteConversation: (conversationId: string, e?: React.MouseEvent) => Promise<void>;
  handleConversationSelect: (conversationId: string) => Promise<void>;
}

export function useConversationManagement({
  selectedAgent,
  onDebugEvent,
}: UseConversationManagementOptions): UseConversationManagementReturn {
  const availableConversations = useDevUIStore((state) => state.availableConversations);
  const setCurrentConversation = useDevUIStore((state) => state.setCurrentConversation);
  const setAvailableConversations = useDevUIStore((state) => state.setAvailableConversations);
  const setChatItems = useDevUIStore((state) => state.setChatItems);
  const setIsStreaming = useDevUIStore((state) => state.setIsStreaming);

  const [conversationError, setConversationError] = useState<ConversationError | null>(null);

  const handleNewConversation = useCallback(async () => {
    if (!selectedAgent) return;

    try {
      const newConversation = await apiClient.createConversation({
        agent_id: selectedAgent.id,
      });
      setCurrentConversation(newConversation);
      setAvailableConversations([newConversation, ...useDevUIStore.getState().availableConversations]);
      setChatItems([]);
      setIsStreaming(false);
      setConversationError(null);
      // Reset conversation usage
      useDevUIStore.setState({ conversationUsage: { total_tokens: 0, message_count: 0 } });

      // Update localStorage cache with new conversation
      const cachedKey = `devui_convs_${selectedAgent.id}`;
      const updated = [newConversation, ...useDevUIStore.getState().availableConversations];
      localStorage.setItem(cachedKey, JSON.stringify(updated));
    } catch (error) {
      const errorMessage = error instanceof Error ? error.message : "Failed to create conversation";
      setConversationError({
        message: errorMessage,
        type: "conversation_creation_error",
      });
    }
  }, [selectedAgent, setCurrentConversation, setAvailableConversations, setChatItems, setIsStreaming]);

  const handleDeleteConversation = useCallback(
    async (conversationId: string, e?: React.MouseEvent) => {
      if (e) {
        e.preventDefault();
        e.stopPropagation();
      }

      if (!confirm("Delete this conversation? This cannot be undone.")) {
        return;
      }

      try {
        const success = await apiClient.deleteConversation(conversationId);
        if (success) {
          const currentAvailable = useDevUIStore.getState().availableConversations;
          const updatedConversations = currentAvailable.filter((c) => c.id !== conversationId);
          setAvailableConversations(updatedConversations);

          // Clear streaming state for deleted conversation
          clearStreamingState(conversationId);

          const currentConv = useDevUIStore.getState().currentConversation;
          if (currentConv?.id === conversationId) {
            if (updatedConversations.length > 0) {
              const nextConversation = updatedConversations[0];
              setCurrentConversation(nextConversation);
              setChatItems([]);
              setIsStreaming(false);
            } else {
              setCurrentConversation(undefined);
              setChatItems([]);
              setIsStreaming(false);
              useDevUIStore.setState({ conversationUsage: { total_tokens: 0, message_count: 0 } });
            }
          }

          onDebugEvent("clear");
        }
      } catch {
        alert("Failed to delete conversation. Please try again.");
      }
    },
    [onDebugEvent, setAvailableConversations, setCurrentConversation, setChatItems, setIsStreaming]
  );

  const handleConversationSelect = useCallback(
    async (conversationId: string) => {
      const conversation = availableConversations.find((c) => c.id === conversationId);
      if (!conversation) return;

      setCurrentConversation(conversation);
      onDebugEvent("clear");

      // Note: The actual item loading and streaming resume logic is handled
      // in the main component to keep this hook focused on CRUD operations
    },
    [availableConversations, onDebugEvent, setCurrentConversation]
  );

  return {
    conversationError,
    setConversationError,
    handleNewConversation,
    handleDeleteConversation,
    handleConversationSelect,
  };
}
