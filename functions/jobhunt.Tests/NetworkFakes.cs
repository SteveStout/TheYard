namespace JobHunt.Functions.Tests;

/// <summary>Hand-written fakes at the network log's ports: rows in a dictionary, Gmail as a set of threads, and a draft port that records.</summary>
public static class NetworkFakes
{
    /// <summary>A clock that always answers one time.</summary>
    public sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        /// <inheritdoc />
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>network.Conversations and network.Passes in memory, keyed the way the table's unique index keys them.</summary>
    public sealed class Rows : IConversationStore
    {
        private long _next = 1;

        /// <summary>The rows by PersonKey.</summary>
        public Dictionary<string, Conversation> Store { get; } = new(StringComparer.Ordinal);

        /// <summary>The pass rows by Central day.</summary>
        public Dictionary<DateOnly, PassRow> Passes { get; } = [];

        public Task<Conversation?> FindAsync(string personKey, CancellationToken ct) => Task.FromResult(Store.GetValueOrDefault(personKey));

        public Task<Conversation?> FindByMatchKeyAsync(string matchKey, CancellationToken ct) =>
            Task.FromResult(Store.Values.Where(r => r.MatchKey == matchKey).OrderBy(r => r.Id).FirstOrDefault());

        public Task<IReadOnlyList<Conversation>> OpenAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Conversation>>([.. Store.Values.Where(r => r.State is ConversationState.Open or ConversationState.Drafted)]);

        public Task<IReadOnlyList<DateTimeOffset>> StartsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<DateTimeOffset>>([.. Store.Values.Select(r => r.FirstTwoWayAt)]);

        public Task<PassRow?> PassAsync(DateOnly centralDay, CancellationToken ct) => Task.FromResult(Passes.GetValueOrDefault(centralDay));

        public Task<long> UpsertAsync(Conversation row, DateTimeOffset at, CancellationToken ct)
        {
            long id = Store.TryGetValue(row.PersonKey, out var stored) ? stored.Id : row.Id > 0 ? row.Id : _next++;
            Store[row.PersonKey] = row with { Id = id, Channel = stored?.Channel ?? row.Channel };
            return Task.FromResult(id);
        }

        public Task SetStateAsync(long id, ConversationState state, string? reason, CancellationToken ct) => Change(id, r => r with { State = state, CloseReason = reason });

        public Task SetTopicAsync(long id, string topicLine, string source, CancellationToken ct) => Change(id, r => r with { TopicLine = topicLine, TopicSource = source });

        public Task SetBlogLineAsync(long id, string blogSlug, string refCode, CancellationToken ct) => Change(id, r => r with { BlogSlug = blogSlug, RefCode = refCode });

        public Task SetDraftAsync(long id, string draftId, Channel channel, DateTimeOffset at, CancellationToken ct) =>
            Change(id, r => r with { State = ConversationState.Drafted, DraftId = draftId, DraftChannel = channel, DraftedAt = at });

        public Task StartPassAsync(DateOnly centralDay, string mode, DateTimeOffset at, CancellationToken ct)
        {
            Passes[centralDay] = new PassRow(centralDay, mode, at, null);
            return Task.CompletedTask;
        }

        public Task FinishPassAsync(DateOnly centralDay, DateTimeOffset at, PassCounts counts, CancellationToken ct)
        {
            Passes[centralDay] = Passes[centralDay] with { FinishedAt = at };
            return Task.CompletedTask;
        }

        private Task Change(long id, Func<Conversation, Conversation> change)
        {
            var row = Store.Values.Single(r => r.Id == id);
            Store[row.PersonKey] = change(row);
            return Task.CompletedTask;
        }
    }

    /// <summary>Gmail as threads by id; every search answers every thread (before its point, when it has one), newest first, and is recorded.</summary>
    public sealed class Gmail : IThreadReader
    {
        /// <summary>The threads by id.</summary>
        public Dictionary<string, ThreadSummary> Threads { get; } = new(StringComparer.Ordinal);

        /// <summary>The threads with a calendar part.</summary>
        public HashSet<string> Calendar { get; } = new(StringComparer.Ordinal);

        /// <summary>Every search asked.</summary>
        public List<ThreadSearch> Searches { get; } = [];

        /// <summary>Every thread read in full.</summary>
        public List<string> FullReads { get; } = [];

        public Task<IReadOnlyList<string>> FindThreadIdsAsync(ThreadSearch search, CancellationToken ct)
        {
            Searches.Add(search);
            return Task.FromResult<IReadOnlyList<string>>([.. Threads.Values.Where(t => search.Before is not { } before || t.Messages.Any(m => m.At < before)).OrderByDescending(t => t.Messages.Max(m => m.At)).Select(t => t.ThreadId).Take(search.Max)]);
        }

        public Task<ThreadSummary> ReadSummaryAsync(string threadId, CancellationToken ct) => Task.FromResult(Threads[threadId]);

        public Task<bool> ReadHasCalendarPartAsync(string threadId, CancellationToken ct)
        {
            FullReads.Add(threadId);
            return Task.FromResult(Calendar.Contains(threadId));
        }
    }

    /// <summary>A draft port that records each call and answers the drafts it holds as existing.</summary>
    public sealed class Drafts : IDraftWriter
    {
        /// <summary>The drafts it holds.</summary>
        public HashSet<string> Existing { get; } = new(StringComparer.Ordinal);

        /// <summary>When true, a draft written is not held, so its read back fails.</summary>
        public bool Forget { get; set; }

        /// <summary>The drafts asked after.</summary>
        public List<string> Asked { get; } = [];

        /// <summary>The drafts written, in order.</summary>
        public List<DraftMail> Written { get; } = [];

        /// <summary>The labels put on messages, as message and label.</summary>
        public List<(string Message, string Label)> Labelled { get; } = [];

        public Task<string> EnsureLabelAsync(string name, CancellationToken ct) => Task.FromResult("L:" + name);

        public Task<(string DraftId, string MessageId)> CreateDraftAsync(DraftMail draft, CancellationToken ct)
        {
            Written.Add(draft);
            string id = $"r-{Written.Count}";
            if (!Forget)
            {
                Existing.Add(id);
            }
            return Task.FromResult((id, $"m-{Written.Count}"));
        }

        public Task LabelAsync(string messageId, string labelId, CancellationToken ct)
        {
            Labelled.Add((messageId, labelId));
            return Task.CompletedTask;
        }

        public Task<bool> DraftExistsAsync(string draftId, CancellationToken ct)
        {
            Asked.Add(draftId);
            return Task.FromResult(Existing.Contains(draftId));
        }
    }

    /// <summary>The blog's feed as a list it is handed, counting its reads.</summary>
    public sealed class Blog(params BlogPost[] posts) : IBlogPosts
    {
        /// <summary>How many times the feed was read.</summary>
        public int Reads { get; private set; }

        public Task<IReadOnlyList<BlogPost>> PostsAsync(CancellationToken ct)
        {
            Reads++;
            return Task.FromResult<IReadOnlyList<BlogPost>>(posts);
        }
    }
}
