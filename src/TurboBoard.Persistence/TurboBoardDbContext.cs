using Microsoft.EntityFrameworkCore;

namespace TurboBoard.Persistence;

public sealed class TurboBoardDbContext(DbContextOptions<TurboBoardDbContext> options)
    : DbContext(options);
