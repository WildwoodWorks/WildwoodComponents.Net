// AI Chat Component JavaScript Functions

window.autoResizeTextarea = (element) => {
    if (!element) return;
    element.style.height = 'auto';
    const newHeight = Math.min(element.scrollHeight, 120);
    element.style.height = newHeight + 'px';
};

window.scrollToBottom = (element) => {
    if (!element) return;
    element.scrollTop = element.scrollHeight;
};

// Scrolls to show the top of the last message in the container
window.scrollToLastMessage = (element) => {
    if (!element) {
        console.warn('scrollToLastMessage: No element provided');
        return;
    }
    
    // Find all assistant messages (AI responses)
    const assistantMessages = element.querySelectorAll('.ai-chat-message-assistant');
    const allMessages = element.querySelectorAll('.ai-chat-message');
    
    console.log('scrollToLastMessage: Found', assistantMessages.length, 'assistant messages,', allMessages.length, 'total messages');
    
    let targetMessage = null;
    
    // Prefer the last assistant message
    if (assistantMessages.length > 0) {
        targetMessage = assistantMessages[assistantMessages.length - 1];
        console.log('scrollToLastMessage: Using last assistant message');
    } else if (allMessages.length > 0) {
        targetMessage = allMessages[allMessages.length - 1];
        console.log('scrollToLastMessage: Using last message (any type)');
    }
    
    if (targetMessage) {
        // Use scrollIntoView with block: 'start' to show top of message
        // Add a small delay to ensure smooth rendering
        setTimeout(() => {
            targetMessage.scrollIntoView({ 
                behavior: 'smooth', 
                block: 'start'
            });
            console.log('scrollToLastMessage: scrollIntoView called on target');
        }, 100);
    } else {
        // Fallback to bottom if no messages found
        console.log('scrollToLastMessage: No messages found, scrolling to bottom');
        element.scrollTop = element.scrollHeight;
    }
};

window.focusElement = (element) => {
    if (!element) return;
    element.focus();
};

window.clearTextareaValue = (element) => {
    if (!element) return;
    element.value = '';
    element.style.height = 'auto';
};

// Set up Enter key handler for chat input that prevents default newline behavior
window.setupChatInputKeyHandler = (element, dotNetRef) => {
    if (!element || !dotNetRef) return;

    // Remove any existing handler first
    if (element._chatKeyHandler) {
        element.removeEventListener('keydown', element._chatKeyHandler);
    }

    element._chatKeyHandler = async (e) => {
        if (e.key === 'Enter' && !e.shiftKey) {
            e.preventDefault();
            try {
                await dotNetRef.invokeMethodAsync('HandleEnterKeyPress');
            } catch (err) {
                console.warn('Failed to invoke HandleEnterKeyPress:', err);
            }
        }
    };

    element.addEventListener('keydown', element._chatKeyHandler);
};

// Clean up the Enter key handler
window.removeChatInputKeyHandler = (element) => {
    if (!element || !element._chatKeyHandler) return;
    element.removeEventListener('keydown', element._chatKeyHandler);
    element._chatKeyHandler = null;
};

window.aiChatInterop = {
    TTS_MAX_LENGTH: 4096,
    _audioQueue: [],
    _isPlayingQueue: false,
    _prefetchedAudio: null,
    _microphonePermissionState: 'unknown',
    _currentChunkAudio: null,
    _currentChunkAudioUrl: null,

    autoResizeTextarea: function (elementRef) {
        if (elementRef) {
            elementRef.style.height = 'auto';
            const newHeight = Math.min(elementRef.scrollHeight, 120);
            elementRef.style.height = newHeight + 'px';
        }
    },

    scrollToBottom: function (elementRef) {
        if (elementRef) {
            elementRef.scrollTop = elementRef.scrollHeight;
        }
    },

    focusInput: function (elementRef) {
        if (elementRef) {
            elementRef.focus();
        }
    },

    copyToClipboard: async function (text) {
        try {
            await navigator.clipboard.writeText(text);
            return true;
        } catch (err) {
            console.error('Failed to copy text: ', err);
            return false;
        }
    },

    downloadText: function (text, filename) {
        const element = document.createElement('a');
        const file = new Blob([text], { type: 'text/plain' });
        element.href = URL.createObjectURL(file);
        element.download = filename;
        document.body.appendChild(element);
        element.click();
        document.body.removeChild(element);
    },

    isSpeechToTextSupported: function () {
        return ('webkitSpeechRecognition' in window) || ('SpeechRecognition' in window);
    },

    checkMicrophonePermission: async function () {
        try {
            if (navigator.permissions && navigator.permissions.query) {
                try {
                    const result = await navigator.permissions.query({ name: 'microphone' });
                    this._microphonePermissionState = result.state;
                    return {
                        state: result.state,
                        canRequest: result.state === 'prompt',
                        isGranted: result.state === 'granted',
                        isDenied: result.state === 'denied'
                    };
                } catch (permError) {
                    console.log('Permissions API query failed:', permError.message);
                }
            }
            return { state: 'unknown', canRequest: true, isGranted: false, isDenied: false };
        } catch (error) {
            return { state: 'error', canRequest: false, isGranted: false, isDenied: false, error: error.message };
        }
    },

    requestMicrophonePermission: async function () {
        try {
            if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
                return { success: false, state: 'unsupported', error: 'Microphone access is not supported.' };
            }
            const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
            stream.getTracks().forEach(track => track.stop());
            this._microphonePermissionState = 'granted';
            return { success: true, state: 'granted', isGranted: true };
        } catch (error) {
            let state = 'denied';
            let errorMessage = 'Microphone permission was denied.';
            if (error.name === 'NotFoundError') {
                state = 'not-found';
                errorMessage = 'No microphone was found on this device.';
            }
            return { success: false, state: state, isGranted: false, isDenied: state === 'denied', error: errorMessage };
        }
    },

    getMicrophonePermissionInstructions: function () {
        const userAgent = navigator.userAgent.toLowerCase();
        if (/windows/.test(userAgent)) {
            return { platform: 'Windows', instructions: 'Click the lock icon in the address bar and allow microphone access.' };
        } else if (/macintosh|mac os x/.test(userAgent)) {
            return { platform: 'macOS', instructions: 'Go to System Preferences > Security & Privacy > Microphone.' };
        }
        return { platform: 'Unknown', instructions: 'Enable microphone access in your browser settings.' };
    },

    startSpeechToText: async function (dotNetRef) {
        if (!this.isSpeechToTextSupported()) {
            return { success: false, error: 'Speech recognition not supported', requiresPermission: false };
        }

        try {
            if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
                return { success: false, error: 'Microphone access not available.', requiresPermission: true };
            }
            const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
            stream.getTracks().forEach(track => track.stop());
        } catch (permError) {
            const instructions = this.getMicrophonePermissionInstructions();
            return { 
                success: false, 
                error: permError.name === 'NotFoundError' ? 'No microphone found.' : 'Microphone permission denied.',
                requiresPermission: true,
                permissionState: permError.name === 'NotFoundError' ? 'not-found' : 'denied',
                instructions: instructions.instructions,
                platform: instructions.platform
            };
        }

        const SpeechRecognition = window.SpeechRecognition || window.webkitSpeechRecognition;
        const recognition = new SpeechRecognition();
        recognition.continuous = true;
        recognition.interimResults = true;
        recognition.lang = 'en-US';

        recognition.onresult = function (event) {
            let interimTranscript = '';
            let finalTranscript = '';
            for (let i = event.resultIndex; i < event.results.length; i++) {
                const transcript = event.results[i][0].transcript;
                if (event.results[i].isFinal) {
                    finalTranscript += transcript;
                } else {
                    interimTranscript += transcript;
                }
            }
            if (finalTranscript) {
                dotNetRef.invokeMethodAsync('OnSpeechToTextResult', finalTranscript, true);
            } else if (interimTranscript) {
                dotNetRef.invokeMethodAsync('OnSpeechToTextResult', interimTranscript, false);
            }
        };

        recognition.onerror = function (event) {
            if (event.error === 'not-allowed') {
                dotNetRef.invokeMethodAsync('OnSpeechToTextPermissionDenied');
            } else if (event.error !== 'no-speech' && event.error !== 'aborted') {
                dotNetRef.invokeMethodAsync('OnSpeechToTextError', event.error);
            }
        };

        recognition.onend = function () {
            dotNetRef.invokeMethodAsync('OnSpeechToTextEnded');
        };

        try {
            recognition.start();
            window._currentSpeechRecognition = recognition;
            return { success: true, requiresPermission: false };
        } catch (error) {
            return { success: false, error: error.message, requiresPermission: false };
        }
    },

    stopSpeechToText: function () {
        if (window._currentSpeechRecognition) {
            window._currentSpeechRecognition.stop();
            window._currentSpeechRecognition = null;
        }
    },

    isTextToSpeechSupported: function () {
        return 'speechSynthesis' in window;
    },

    speakText: function (text, dotNetRef) {
        if (!this.isTextToSpeechSupported()) {
            if (dotNetRef) dotNetRef.invokeMethodAsync('OnTextToSpeechError', 'Speech synthesis not supported');
            return false;
        }
        window.speechSynthesis.cancel();
        const utterance = new SpeechSynthesisUtterance(text);
        utterance.lang = 'en-US';
        utterance.onstart = () => { if (dotNetRef) dotNetRef.invokeMethodAsync('OnTextToSpeechStarted'); };
        utterance.onend = () => { if (dotNetRef) dotNetRef.invokeMethodAsync('OnTextToSpeechEnded'); };
        utterance.onerror = (event) => { if (dotNetRef) dotNetRef.invokeMethodAsync('OnTextToSpeechError', event.error); };
        window.speechSynthesis.speak(utterance);
        return true;
    },

    speakTextViaApi: async function (apiBaseUrl, authToken, text, voice, speed, dotNetRef, configurationId) {
        console.log('?? TTS API: Starting speech synthesis, text length:', text?.length || 0);

        try {
            this.stopSpeakingViaApi();

            if (dotNetRef) {
                dotNetRef.invokeMethodAsync('OnTextToSpeechStarted');
            }

            const FIRST_CHUNK_MAX = 200;
            const SUBSEQUENT_CHUNK_MAX = 800;
            const chunks = this.splitTextForProgressivePlayback(text, FIRST_CHUNK_MAX, SUBSEQUENT_CHUNK_MAX);
            
            if (chunks.length === 0) {
                if (dotNetRef) dotNetRef.invokeMethodAsync('OnTextToSpeechEnded');
                return true;
            }

            const success = await this.playProgressiveAudio(apiBaseUrl, authToken, chunks, voice, speed, dotNetRef, configurationId);
            
            if (!success && dotNetRef) {
                dotNetRef.invokeMethodAsync('OnTextToSpeechError', 'TTS playback failed');
            }
            
            return success;
        } catch (error) {
            console.error('?? TTS API: Error:', error);
            if (dotNetRef) dotNetRef.invokeMethodAsync('OnTextToSpeechError', 'TTS failed: ' + error.message);
            return false;
        }
    },

    splitTextForProgressivePlayback: function (text, firstChunkMax, subsequentChunkMax) {
        if (!text || text.trim().length === 0) return [];
        const chunks = [];
        let remainingText = text.trim();
        let isFirstChunk = true;

        while (remainingText.length > 0) {
            const maxLength = isFirstChunk ? firstChunkMax : subsequentChunkMax;
            if (remainingText.length <= maxLength) {
                chunks.push(remainingText);
                break;
            }
            let breakPoint = maxLength;
            const sentenceBreakers = ['. ', '! ', '? ', '.\n', '!\n', '?\n'];
            let bestBreak = -1;
            for (const breaker of sentenceBreakers) {
                const idx = remainingText.lastIndexOf(breaker, maxLength);
                if (idx > bestBreak && idx > maxLength * 0.3) bestBreak = idx + breaker.length;
            }
            if (bestBreak > 0) {
                breakPoint = bestBreak;
            } else {
                const spaceBreak = remainingText.lastIndexOf(' ', maxLength);
                if (spaceBreak > maxLength * 0.5) breakPoint = spaceBreak + 1;
            }
            chunks.push(remainingText.substring(0, breakPoint).trim());
            remainingText = remainingText.substring(breakPoint).trim();
            isFirstChunk = false;
        }
        return chunks.filter(c => c.length > 0);
    },

    playProgressiveAudio: async function (apiBaseUrl, authToken, chunks, voice, speed, dotNetRef, configurationId) {
        this._audioQueue = [];
        this._isPlayingQueue = true;
        this._prefetchedAudio = null;

        try {
            for (let i = 0; i < chunks.length; i++) {
                if (!this._isPlayingQueue) break;

                const isLast = (i === chunks.length - 1);
                let nextChunkPromise = null;
                
                if (!isLast && this._isPlayingQueue) {
                    nextChunkPromise = this.fetchAudioChunk(apiBaseUrl, authToken, chunks[i + 1], voice, speed, configurationId);
                }

                let audioData = this._prefetchedAudio || await this.fetchAudioChunk(apiBaseUrl, authToken, chunks[i], voice, speed, configurationId);
                this._prefetchedAudio = null;

                if (!audioData) {
                    if (dotNetRef) dotNetRef.invokeMethodAsync('OnTextToSpeechError', 'Failed to get audio');
                    return false;
                }

                const playSuccess = await this.playAudioBlob(audioData, isLast ? dotNetRef : null, isLast);
                if (!playSuccess) return false;

                if (nextChunkPromise) this._prefetchedAudio = await nextChunkPromise;
            }
            return true;
        } finally {
            this._isPlayingQueue = false;
            this._prefetchedAudio = null;
        }
    },

    fetchAudioChunk: async function (apiBaseUrl, authToken, text, voice, speed, configurationId) {
        try {
            const response = await fetch(`${apiBaseUrl}/tts/synthesize`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json', 'Authorization': `Bearer ${authToken}` },
                body: JSON.stringify({ text, voice: voice || 'alloy', speed: speed || 1.0, format: 'mp3', configurationId })
            });
            if (!response.ok) return null;
            const audioBlob = await response.blob();
            return audioBlob.size > 0 ? audioBlob : null;
        } catch (error) {
            console.error('TTS fetch error:', error);
            return null;
        }
    },

    playAudioBlob: function (audioBlob, dotNetRef, notifyOnEnd) {
        return new Promise((resolve) => {
            const audioUrl = URL.createObjectURL(audioBlob);
            const audio = new Audio(audioUrl);
            window._currentTTSAudio = audio;
            window._currentTTSAudioUrl = audioUrl;

            audio.onended = () => {
                URL.revokeObjectURL(audioUrl);
                window._currentTTSAudio = null;
                window._currentTTSAudioUrl = null;
                if (notifyOnEnd && dotNetRef) dotNetRef.invokeMethodAsync('OnTextToSpeechEnded');
                resolve(true);
            };

            audio.onerror = () => {
                URL.revokeObjectURL(audioUrl);
                window._currentTTSAudio = null;
                window._currentTTSAudioUrl = null;
                if (dotNetRef) dotNetRef.invokeMethodAsync('OnTextToSpeechError', 'Audio playback error');
                resolve(false);
            };

            audio.play().catch(() => resolve(false));
        });
    },

    /**
     * Play a single audio chunk from base64 data with completion callbacks.
     * Used for progressive TTS playback from C#.
     * @param {string} audioBase64 - Base64-encoded audio data
     * @param {string} contentType - MIME type (e.g., 'audio/mpeg')
     * @param {object} dotNetRef - .NET object reference for callbacks (OnChunkEnded, OnChunkError)
     * @returns {Promise<boolean>} - Whether playback started successfully
     */
    playAudioChunk: function (audioBase64, contentType, dotNetRef) {
        return new Promise((resolve) => {
            try {
                console.log('?? TTS: Playing audio chunk, size:', audioBase64?.length || 0);
                
                // Stop any currently playing chunk audio
                if (this._currentChunkAudio) {
                    try {
                        this._currentChunkAudio.pause();
                        this._currentChunkAudio.src = '';
                    } catch (e) { }
                }
                if (this._currentChunkAudioUrl) {
                    URL.revokeObjectURL(this._currentChunkAudioUrl);
                }
                
                // Convert base64 to blob
                const byteCharacters = atob(audioBase64);
                const byteNumbers = new Array(byteCharacters.length);
                for (let i = 0; i < byteCharacters.length; i++) {
                    byteNumbers[i] = byteCharacters.charCodeAt(i);
                }
                const byteArray = new Uint8Array(byteNumbers);
                const audioBlob = new Blob([byteArray], { type: contentType || 'audio/mpeg' });
                
                // Create audio element and play
                const audioUrl = URL.createObjectURL(audioBlob);
                const audio = new Audio(audioUrl);
                this._currentChunkAudio = audio;
                this._currentChunkAudioUrl = audioUrl;
                
                // Also set as current TTS audio for stop functionality
                window._currentTTSAudio = audio;
                window._currentTTSAudioUrl = audioUrl;

                audio.onended = () => {
                    console.log('?? TTS: Chunk playback ended');
                    URL.revokeObjectURL(audioUrl);
                    this._currentChunkAudio = null;
                    this._currentChunkAudioUrl = null;
                    if (dotNetRef) {
                        dotNetRef.invokeMethodAsync('OnChunkEnded');
                    }
                };

                audio.onerror = (e) => {
                    console.error('?? TTS: Chunk playback error:', e);
                    URL.revokeObjectURL(audioUrl);
                    this._currentChunkAudio = null;
                    this._currentChunkAudioUrl = null;
                    if (dotNetRef) {
                        dotNetRef.invokeMethodAsync('OnChunkError', 'Audio playback error');
                    }
                };

                audio.play()
                    .then(() => {
                        console.log('?? TTS: Chunk playback started');
                        resolve(true);
                    })
                    .catch((error) => {
                        console.error('?? TTS: Failed to start chunk playback:', error);
                        if (dotNetRef) {
                            dotNetRef.invokeMethodAsync('OnChunkError', 'Failed to start audio: ' + error.message);
                        }
                        resolve(false);
                    });
            } catch (error) {
                console.error('?? TTS: Error in playAudioChunk:', error);
                if (dotNetRef) {
                    dotNetRef.invokeMethodAsync('OnChunkError', 'Audio decode error: ' + error.message);
                }
                resolve(false);
            }
        });
    },

    /**
     * Play audio from base64 data - no CORS required!
     * This is called from C# after fetching audio via HttpClient.
     * @param {string} audioBase64 - Base64-encoded audio data
     * @param {string} contentType - MIME type (e.g., 'audio/mpeg')
     * @param {object} dotNetRef - .NET object reference for callbacks
     * @returns {Promise<boolean>} - Success status
     */
    playAudioFromBase64: function (audioBase64, contentType, dotNetRef) {
        return new Promise((resolve) => {
            try {
                console.log('?? TTS: Playing audio from base64, size:', audioBase64?.length || 0);
                
                // Stop any currently playing audio
                this.stopSpeakingViaApi();
                
                // Convert base64 to blob
                const byteCharacters = atob(audioBase64);
                const byteNumbers = new Array(byteCharacters.length);
                for (let i = 0; i < byteCharacters.length; i++) {
                    byteNumbers[i] = byteCharacters.charCodeAt(i);
                }
                const byteArray = new Uint8Array(byteNumbers);
                const audioBlob = new Blob([byteArray], { type: contentType || 'audio/mpeg' });
                
                // Create audio element and play
                const audioUrl = URL.createObjectURL(audioBlob);
                const audio = new Audio(audioUrl);
                window._currentTTSAudio = audio;
                window._currentTTSAudioUrl = audioUrl;

                audio.onended = () => {
                    console.log('?? TTS: Audio playback ended');
                    URL.revokeObjectURL(audioUrl);
                    window._currentTTSAudio = null;
                    window._currentTTSAudioUrl = null;
                    if (dotNetRef) dotNetRef.invokeMethodAsync('OnTextToSpeechEnded');
                    resolve(true);
                };

                audio.onerror = (e) => {
                    console.error('?? TTS: Audio playback error:', e);
                    URL.revokeObjectURL(audioUrl);
                    window._currentTTSAudio = null;
                    window._currentTTSAudioUrl = null;
                    if (dotNetRef) dotNetRef.invokeMethodAsync('OnTextToSpeechError', 'Audio playback error');
                    resolve(false);
                };

                // Notify that playback has started
                if (dotNetRef) dotNetRef.invokeMethodAsync('OnTextToSpeechStarted');

                audio.play().catch((error) => {
                    console.error('?? TTS: Failed to start playback:', error);
                    if (dotNetRef) dotNetRef.invokeMethodAsync('OnTextToSpeechError', 'Failed to start audio: ' + error.message);
                    resolve(false);
                });
            } catch (error) {
                console.error('?? TTS: Error in playAudioFromBase64:', error);
                if (dotNetRef) dotNetRef.invokeMethodAsync('OnTextToSpeechError', 'Audio decode error: ' + error.message);
                resolve(false);
            }
        });
    },

    stopSpeaking: function () {
        console.log('?? TTS: Stopping all audio');
        this.stopSpeakingViaApi();
    },

    stopSpeakingViaApi: function () {
        this._isPlayingQueue = false;
        this._audioQueue = [];
        this._prefetchedAudio = null;
        
        if (window._currentTTSAudio) {
            try {
                window._currentTTSAudio.pause();
                window._currentTTSAudio.src = '';
            } catch (e) { }
            window._currentTTSAudio = null;
        }
        if (window._currentTTSAudioUrl) {
            URL.revokeObjectURL(window._currentTTSAudioUrl);
            window._currentTTSAudioUrl = null;
        }
    },

    isSpeaking: function () {
        if (this.isTextToSpeechSupported() && window.speechSynthesis.speaking) return true;
        if (window._currentTTSAudio && !window._currentTTSAudio.paused) return true;
        return false;
    },

    fetchTTSVoices: async function (apiBaseUrl, authToken, configurationId) {
        try {
            let url = `${apiBaseUrl}/tts/voices`;
            if (configurationId) url = `${apiBaseUrl}/tts/voices/configuration/${configurationId}`;
            
            const response = await fetch(url, {
                method: 'GET',
                headers: { 'Authorization': `Bearer ${authToken}`, 'Content-Type': 'application/json' }
            });
            
            if (!response.ok) return null;
            return await response.json();
        } catch (error) {
            console.error('Error fetching voices:', error);
            return null;
        }
    },

    setItem: function (key, value) {
        try { localStorage.setItem(key, JSON.stringify(value)); return true; } catch (e) { return false; }
    },

    getItem: function (key) {
        try { const item = localStorage.getItem(key); return item ? JSON.parse(item) : null; } catch (e) { return null; }
    },

    removeItem: function (key) {
        try { localStorage.removeItem(key); return true; } catch (e) { return false; }
    }
};

document.addEventListener('DOMContentLoaded', function () {
    console.log('AI Chat Component JavaScript loaded');
});
