using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Company.Api.Data;

/// <summary>
/// The audit log as a chain: each event carries the hash of the one before and its own hash over its fields. Changing, removing or
/// inserting an event afterwards (with a database tool, say) breaks every hash after it, which <see cref="VerifyAsync"/> finds.
/// It proves that the log was not changed; it does not prevent it (for that, see the database rights in the IT guide).
/// </summary>
public static class AuditChain
{
    public static string Compute(string? previousHash, AuditEvent e)
    {
        // The moment is written to the microsecond, which is what the database keeps
        var text = string.Join("\n", previousHash ?? "", e.AtUtc.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff", CultureInfo.InvariantCulture),
            e.Actor, e.Action, e.EntityType, e.EntityId, e.Details ?? "");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    /// <summary>Gives the new events their place in the chain, in the order they were added</summary>
    public static void Link(IReadOnlyList<AuditEvent> added, string? lastHash)
    {
        var previous = lastHash;
        foreach (var e in added)
        {
            e.PreviousHash = previous;
            e.Hash = Compute(previous, e);
            previous = e.Hash;
        }
    }

    public sealed record Result(long Checked, long Unchained, long? FirstBrokenId, string? Problem)
    {
        public bool IsIntact => FirstBrokenId == null;
    }

    /// <summary>Walks the log from the first event and checks the links and the hashes</summary>
    public static async Task<Result> VerifyAsync(CompanyDbContext db)
    {
        string? previous = null;
        long checkedCount = 0, unchained = 0;
        var started = false;

        await foreach (var e in db.AuditEvents.AsNoTracking().OrderBy(x => x.Id).AsAsyncEnumerable())
        {
            if (e.Hash == null)
            {
                // Written before the chain existed; they may only come first
                if (started) return new Result(checkedCount, unchained, e.Id, "An event without a hash follows chained events.");
                unchained++;
                continue;
            }

            started = true;
            if (e.PreviousHash != previous) return new Result(checkedCount, unchained, e.Id, "The event does not follow the one before it: an event was removed, inserted or changed.");
            if (Compute(e.PreviousHash, e) != e.Hash) return new Result(checkedCount, unchained, e.Id, "The content of the event does not match its hash: it was changed.");

            previous = e.Hash;
            checkedCount++;
        }

        return new Result(checkedCount, unchained, null, null);
    }
}
