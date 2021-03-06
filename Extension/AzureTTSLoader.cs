﻿using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using ChatdollKit.Model;
using ChatdollKit.Network;


namespace ChatdollKit.Extension
{
    public class AzureTTSLoader : WebVoiceLoaderBase
    {
        public override VoiceLoaderType Type { get; } = VoiceLoaderType.TTS;
        public string _Name = "Azure";
        public override string Name
        {
            get
            {
                return _Name;
            }
        }
        public bool _IsDefault = true;
        public override bool IsDefault
        {
            get
            {
                return _IsDefault;
            }
            set
            {
                _IsDefault = value;
            }
        }

        public string ApiKey;
        public string Region = "japanwest";
        public string Language = "ja-JP";
        public string Gender = "Female";
        public string SpeakerName = "ja-JP-HarukaRUS";
        public AudioType AudioType = AudioType.WAV;

        protected override async Task<AudioClip> DownloadAudioClipAsync(Voice voice)
        {
            var url = $"https://{Region}.tts.speech.microsoft.com/cognitiveservices/v1";
            using (var www = UnityWebRequestMultimedia.GetAudioClip(url, AudioType))
            {
                www.timeout = 10;
                www.method = "POST";

                // Header
                www.SetRequestHeader("X-Microsoft-OutputFormat", AudioType == AudioType.WAV ? "riff-16khz-16bit-mono-pcm" : "audio-16khz-128kbitrate-mono-mp3");
                www.SetRequestHeader("Content-Type", "application/ssml+xml");
                www.SetRequestHeader("Ocp-Apim-Subscription-Key", ApiKey);

                // Body
                var ttsLanguage = voice.GetTTSParam("language") as string ?? Language;
                var ttsGender = voice.GetTTSParam("gender") as string ?? Gender;
                var ttsSpeakerName = voice.GetTTSParam("speakerName") as string ?? SpeakerName;
                var text = $"<speak version='1.0' xml:lang='{ttsLanguage}'><voice xml:lang='{ttsLanguage}' xml:gender='{ttsGender}' name='{ttsSpeakerName}'>{voice.Text}</voice></speak>";
                www.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(text));

                // Send request
                await www.SendWebRequest();

                if (www.isNetworkError || www.isHttpError)
                {
                    Debug.LogError($"Error occured while processing text-to-speech voice: {www.error}");
                }
                else if (www.isDone)
                {
                    var clip = DownloadHandlerAudioClip.GetContent(www);

                    if (!string.IsNullOrEmpty(voice.Name) && clip != null)
                    {
                        // Cache if name is set
                        audioCache[voice.Name] = clip;
                    }

                    return clip;
                }
            }
            return null;
        }
    }
}
