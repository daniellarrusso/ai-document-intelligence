using AiDocumentIntelligence.Domain;
using Microsoft.EntityFrameworkCore;

namespace AiDocumentIntelligence.Infrastructure;

public class UserRepository : IUserRepository
{
    private readonly AppDbContext _context;

    public UserRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task UpsertAsync(AppUser user, CancellationToken cancellationToken = default)
    {
        var existing = await _context.Users.FirstOrDefaultAsync(u => u.Id == user.Id, cancellationToken);
        if (existing == null)
        {
            _context.Users.Add(user);
        }
        else
        {
            existing.DisplayName = user.DisplayName;
            existing.Email = user.Email;
            existing.IsHandler = user.IsHandler;
            existing.LastSeenAt = user.LastSeenAt;
        }

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (existing == null)
        {
            // Two first requests from the same user raced to insert; the other one won, which is fine.
            _context.Entry(user).State = EntityState.Detached;
        }
    }

    public async Task<List<AppUser>> GetHandlersAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Users
            .AsNoTracking()
            .Where(u => u.IsHandler)
            .OrderBy(u => u.DisplayName)
            .ThenBy(u => u.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<AppUser?> FindHandlerAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == id && u.IsHandler, cancellationToken);
    }
}
