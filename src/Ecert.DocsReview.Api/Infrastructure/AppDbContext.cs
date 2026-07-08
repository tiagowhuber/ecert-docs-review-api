using Microsoft.EntityFrameworkCore;

namespace Ecert.DocsReview.Api.Infrastructure;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }
}
