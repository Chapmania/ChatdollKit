﻿using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using ChatdollKit.Network;


namespace ChatdollKit.IO
{
    public class WakeWordListenerBase : VoiceRecorderBase
    {
        [Header("WakeWord Settings")]
        public List<string> WakeWords;
        public List<string> CancelWords;
        public List<string> IgnoreWords = new List<string>() { "。", "、", "？", "！" };
        public int PrefixAllowance = 4;
        public int SuffixAllowance = 4;
        public Func<string, string> ExtractWakeWord;
        public Func<string, string> ExtractCancelWord;
        public Func<string, Task> OnRecognizedAsync;
        public Func<Task> OnWakeAsync;
        public Func<Task> OnCancelAsync;
        public bool AutoStart = true;

        [Header("Test and Debug")]
        public bool PrintResult = false;

        [Header("Voice Recorder Settings")]
        public float VoiceDetectionThreshold = 0.1f;
        public float VoiceDetectionMinimumLength = 0.2f;
        public float SilenceDurationToEndRecording = 0.3f;

        public Action OnListeningStart;
        public Action OnListeningStop;
        public Action OnRecordingStart = () => { Debug.Log("Recording wakeword started"); };
        public Action<float> OnDetectVoice;
        public Action<AudioClip> OnRecordingEnd = (a) => { Debug.Log("Recording wakeword ended"); };
        public Action<Exception> OnError = (e) => { Debug.LogError($"Recording wakeword error: {e.Message}\n{e.StackTrace}"); };

        // Private and protected members for recording voice and recognize task
        private CancellationTokenSource cancellationTokenSource;
        protected ChatdollHttp client = new ChatdollHttp();

        private void Start()
        {
            if (AutoStart)
            {
#pragma warning disable CS4014
                StartListeningAsync();
#pragma warning restore CS4014
            }
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            cancellationTokenSource?.Cancel();
        }

        public async Task StartListeningAsync()
        {
            if (IsListening)
            {
                Debug.LogWarning("WakeWordListener is already listening");
                return;
            }

            StartListening();   // Start recorder here to asure that GetVoiceAsync will be called after recorder started

            try
            {
                if (OnWakeAsync == null)
                {
                    Debug.LogError("OnWakeAsync must be set");
#pragma warning disable CS1998
                    OnWakeAsync = async () => { Debug.LogWarning("Nothing is invoked by wakeword. Set Func to OnWakeAsync."); };
#pragma warning restore CS1998
                }

                cancellationTokenSource = new CancellationTokenSource();
                var token = cancellationTokenSource.Token;

                while (!token.IsCancellationRequested)
                {
                    voiceDetectionThreshold = VoiceDetectionThreshold;
                    voiceDetectionMinimumLength = VoiceDetectionMinimumLength;
                    silenceDurationToEndRecording = SilenceDurationToEndRecording;
                    onListeningStart = OnListeningStart;
                    onListeningStop = OnListeningStop;
                    onRecordingStart = OnRecordingStart;
                    onDetectVoice = OnDetectVoice;
                    onRecordingEnd = OnRecordingEnd;
                    onError = OnError;

                    var voiceRecorderResponse = await GetVoiceAsync(0.0f, token);
                    if (voiceRecorderResponse != null && voiceRecorderResponse.Voice != null)
                    {
#pragma warning disable CS4014
                        ProcessVoiceAsync(voiceRecorderResponse.Voice); // Do not await to continue listening
#pragma warning restore CS4014
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error occured in listening wakeword: {ex.Message}\n{ex.StackTrace}");
            }
            finally
            {
                StopListening();
            }
        }

        private async Task ProcessVoiceAsync(AudioClip voice)
        {
            // Recognize speech
            var recognizedText = await RecognizeSpeechAsync(voice);
            if (OnRecognizedAsync != null)
            {
                await OnRecognizedAsync(recognizedText);
            }
            if (PrintResult)
            {
                Debug.Log($"Recognized(WakeWordListener): {recognizedText}");
            }

            if (!string.IsNullOrEmpty((ExtractWakeWord ?? ExtractWakeWordDefault).Invoke(recognizedText)))
            {
                try
                {
                    await OnWakeAsync();
                }
                catch (Exception ex)
                {
                    Debug.LogError($"OnWakeAsync failed: {ex.Message}\n{ex.StackTrace}");
                }
            }
            else if (!string.IsNullOrEmpty((ExtractCancelWord ?? ExtractCancelWordDefault).Invoke(recognizedText)))
            {
                try
                {
                    if (OnCancelAsync != null)
                    {
                        await OnCancelAsync();
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"OnCancelAsync failed: {ex.Message}\n{ex.StackTrace}");
                }
            }
        }

        public string ExtractWakeWordDefault(string text)
        {
            foreach (var iw in IgnoreWords)
            {
                text = text.Replace(iw, string.Empty);
            }

            foreach (var ww in WakeWords)
            {
                if (text.Contains(ww))
                {
                    var prefix = text.Substring(0, text.IndexOf(ww));
                    var suffix = text.Substring(text.IndexOf(ww) + ww.Length);

                    if (prefix.Length <= 4 && suffix.Length <= 4)
                    {
                        return ww;
                    }
                }
            }

            return string.Empty;
        }

        public string ExtractCancelWordDefault(string text)
        {
            foreach (var iw in IgnoreWords)
            {
                text = text.Replace(iw, string.Empty);
            }

            foreach (var cw in CancelWords)
            {
                if (text == cw)
                {
                    return cw;
                }
            }

            return string.Empty;
        }

#pragma warning disable CS1998
        protected virtual async Task<string> RecognizeSpeechAsync(AudioClip recordedVoice)
        {
            throw new NotImplementedException("RecognizeSpeechAsync method should be implemented at the sub class of WakeWordListenerBase");
        }
#pragma warning restore CS1998
    }
}
