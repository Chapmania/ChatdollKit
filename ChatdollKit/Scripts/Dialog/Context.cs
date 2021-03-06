﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;


namespace ChatdollKit.Dialog
{
    public class Context
    {
        public string Id { get; }
        public string UserId { get; }
        public DateTime Timestamp { get; set; }
        public bool IsNew { get; set; }
        public Topic Topic { get; set; }
        public Dictionary<string, object> Data { get; set; }
        [JsonIgnore]
        public Func<Context, Task> saveFunc { get; set; }

        public Context(string userId, string id = null, Func<Context, Task> saveFunc = null)
        {
            Id = id == null ? Guid.NewGuid().ToString() : id;
            UserId = userId;
            Timestamp = DateTime.UtcNow;
            IsNew = true;
            Topic = new Topic();
            Data = new Dictionary<string, object>();
            this.saveFunc = saveFunc;
        }

        public void Clear()
        {
            Topic = new Topic();
            Data = new Dictionary<string, object>();
        }

        public async Task SaveAsync()
        {
            await saveFunc?.Invoke(this);
        }
    }

    public class Topic
    {
        public string Name { get; set; }
        public string Status { get; set; }
        public bool IsNew { get; set; }
        public bool ContinueTopic { get; set; }
        public Topic Previous { get; }
        public Priority Priority { get; set; }
        public RequestType RequiredRequestType { get; set; }

        public Topic()
        {
            IsNew = true;
            ContinueTopic = false;
            RequiredRequestType = RequestType.Voice;
        }
    }
}
