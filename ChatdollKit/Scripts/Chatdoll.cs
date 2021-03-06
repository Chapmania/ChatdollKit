﻿using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using ChatdollKit.Dialog;
using ChatdollKit.Model;


namespace ChatdollKit
{
    [RequireComponent(typeof(ModelController))]
    public class Chatdoll : MonoBehaviour
    {
        // Conversation
        public IUserStore UserStore { get; private set; }
        public IContextStore ContextStore { get; private set; }
        public IDialogRouter DialogRouter { get; private set; }
        public Dictionary<RequestType, IRequestProvider> RequestProviders { get; private set; }

        // Model
        public ModelController ModelController { get; set; }

        // TokenSource to cancel talking
        private CancellationTokenSource chatTokenSource;

        // Actions for each status
#pragma warning disable CS1998
        public Func<User, Context, CancellationToken, Task> OnPromptAsync
            = async (u, c, t) => { Debug.LogWarning("Chatdoll.OnPromptAsync is not implemented"); };
        public Func<Request, Context, CancellationToken, Task> OnNoIntentAsync
            = async (r, c, t) => { Debug.LogWarning("Chatdoll.OnNoIntentAsync is not implemented"); };
        public Func<Request, Context, CancellationToken, Task> OnErrorAsync
            = async (r, c, t) => { Debug.LogWarning("Chatdoll.OnErrorAsync is not implemented"); };
#pragma warning restore CS1998

        // Awake
        private void Awake()
        {
            // Use local store when no UserStore attached
            UserStore = gameObject.GetComponent<IUserStore>() ?? gameObject.AddComponent<LocalUserStore>();

            // Use local store when no ContextStore attached
            ContextStore = gameObject.GetComponent<IContextStore>() ?? gameObject.AddComponent<MemoryContextStore>();

            // Register request providers for each input type
            RequestProviders = new Dictionary<RequestType, IRequestProvider>();
            var requestProviders = gameObject.GetComponents<IRequestProvider>();
            if (requestProviders != null)
            {
                foreach (var rp in requestProviders)
                {
                    if (((MonoBehaviour)rp).enabled)
                    {
                        RequestProviders[rp.RequestType] = rp;
                    }
                }
            }
            else
            {
                Debug.LogError("RequestProviders are missing");
            }

            // Configure router
            DialogRouter = gameObject.GetComponent<IDialogRouter>() ?? gameObject.AddComponent<StaticDialogRouter>();
            DialogRouter.Configure();

            // Register intents and its processor
            var dialogProcessors = gameObject.GetComponents<IDialogProcessor>();
            if (dialogProcessors != null)
            {
                foreach (var dp in dialogProcessors)
                {
                    DialogRouter.RegisterIntent(dp.TopicName, dp);
                    Debug.Log($"Intent '{dp.TopicName}' registered successfully");
                }
            }
            else
            {
                Debug.LogError("DialogProcessors are missing");
            }

            // ModelController
            ModelController = gameObject.GetComponent<ModelController>();
        }

        // OnDestroy
        private void OnDestroy()
        {
            chatTokenSource?.Cancel();
        }

        // Start chatting loop
        public async Task StartChatAsync(string userId, Dictionary<string, object> payloads = null)
        {
            // Get cancellation token
            var token = GetChatToken();

            // Get user
            var user = await UserStore.GetUserAsync(userId);
            if (user == null || string.IsNullOrEmpty(user.Id))
            {
                Debug.LogError($"Error occured in getting user: {userId}");
                await OnErrorAsync(null, null, token);
                return;
            }

            // Get context
            var context = await ContextStore.GetContextAsync(user.Id);
            if (context == null)
            {
                Debug.LogError($"Error occured in getting context: {user.Id}");
                await OnErrorAsync(null, null, token);
                return;
            }

            // Prompt
            try
            {
                await OnPromptAsync(user, context, token);
            }
            catch (TaskCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error occured in speaking prompt: {ex.Message}\n{ex.StackTrace}");
                // Restart idling animation and reset face expression
                _ = ModelController?.StartIdlingAsync();
                _ = ModelController?.SetDefaultFace();
                return;
            }

            // Chat loop. Exit when session ends, canceled or error occures
            while (true)
            {
                if (token.IsCancellationRequested) { return; }

                // Get request (microphone / camera / QR code, etc)
                var request = await RequestProviders[context.Topic.RequiredRequestType].GetRequestAsync(user, context, token);

                try
                {
                    if (!request.IsSet() || request.IsCanceled)
                    {
                        // Clear context when request is not set or canceled
                        context.Clear();
                        await ContextStore.SaveContextAsync(context);
                        return;
                    }

                    if (token.IsCancellationRequested) { return; }

                    // Extract intent
                    await DialogRouter.ExtractIntentAsync(request, context, token);
                    if (string.IsNullOrEmpty(request.Intent) && string.IsNullOrEmpty(context.Topic.Name))
                    {
                        // Just exit loop without clearing context when NoIntent
                        await OnNoIntentAsync(request, context, token);
                        return;
                    }
                    else
                    {
                        Debug.Log($"Intent:{request.Intent}({request.IntentPriority.ToString()})");
                        if (request.Entities.Count > 0)
                        {
                            var entitiesString = "Entities:";
                            foreach (var kv in request.Entities)
                            {
                                var v = kv.Value != null ? kv.Value.ToString() : "null";
                                entitiesString += $"\n - {kv.Key}: {v}";
                            }
                            Debug.Log(entitiesString);
                        }
                    }
                    if (token.IsCancellationRequested) { return; }

                    // Get dialog to process intent / topic
                    var dialogProcessor = DialogRouter.Route(request, context, token);
                    if (token.IsCancellationRequested) { return; }

                    // Start showing waiting animation
                    var waitingAnimationTask = dialogProcessor.ShowWaitingAnimationAsync(request, context, token);

                    // Process dialog
                    var dialogResponse = await dialogProcessor.ProcessAsync(request, context, token);
                    if (token.IsCancellationRequested) { return; }

                    // Wait for waiting animation before show response of dialog
                    // TODO: Enable to cancel waitingAnimation instead of await when ProcessAsync ends.
                    await waitingAnimationTask;
                    if (token.IsCancellationRequested) { return; }

                    // Show response of dialog
                    await dialogProcessor.ShowResponseAsync(dialogResponse, request, context, token);
                    if (token.IsCancellationRequested) { return; }

                    // Post process
                    if (context.Topic.ContinueTopic)
                    {
                        // Save user
                        await UserStore.SaveUserAsync(user);
                        // Save context
                        await ContextStore.SaveContextAsync(context);
                        // Update properties for next
                        context.IsNew = false;
                        context.Topic.IsNew = false;
                        context.Topic.ContinueTopic = false;
                    }
                    else
                    {
                        // Clear context data and topic when topic doesn't continue
                        context.Clear();
                        await ContextStore.SaveContextAsync(context);
                        return;
                    }
                }
                catch (TaskCanceledException)
                {
                    await ContextStore.DeleteContextAsync(user.Id);
                    return;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Error occured in processing chat: {ex.Message}\n{ex.StackTrace}");
                    if (!token.IsCancellationRequested)
                    {
                        // Stop running animation and voice then get new token to say error
                        token = GetChatToken();
                        await OnErrorAsync(request, context, token);
                        await ContextStore.DeleteContextAsync(user.Id);
                    }
                    return;
                }
                finally
                {
                    if (!token.IsCancellationRequested)
                    {
                        // Restart idling animation and reset face expression 
                        _ = ModelController?.StartIdlingAsync();
                        _ = ModelController?.SetDefaultFace();
                    }
                    else
                    {
                        // Clear context when canceled
                        context.Clear();
                        await ContextStore.SaveContextAsync(context);
                    }
                }
            }
        }

        // Stop chat
        public void StopChat(bool waitVoice = false, bool startIdling = true)
        {
            // Cancel the tasks and dispose the token source
            if (chatTokenSource != null)
            {
                chatTokenSource.Cancel();
                chatTokenSource.Dispose();
                chatTokenSource = null;
            }

            // Stop speaking immediately if not wait
            if (!waitVoice)
            {
                ModelController?.StopSpeech();
            }

            // Stop animation (and start idling if startDefaultAnimation is true)
            if (startIdling)
            {
                _ = ModelController?.StartIdlingAsync();
            }
        }

        // Get cancellation token for tasks invoked in chat
        private CancellationToken GetChatToken()
        {
            // Stop current chat after the phrase now speaking without starting idling animation
            StopChat(true, false);

            // Create new TokenSource and return its token
            chatTokenSource = new CancellationTokenSource();
            return chatTokenSource.Token;
        }
    }
}
