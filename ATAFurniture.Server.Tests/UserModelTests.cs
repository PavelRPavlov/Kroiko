using ATAFurniture.Server.DataAccess;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ATAFurniture.Server.Tests;

/// <summary>
/// Moving <c>User</c> out of the domain and retyping <c>LastSelectedCompany</c> (phase 02 step 1) must not
/// change the database schema: the model still matches the last migration's snapshot, so no migration is due.
/// </summary>
public sealed class UserModelTests
{
    [Fact]
    public void The_EF_model_has_no_changes_pending_a_migration()
    {
        // Builds the model only; nothing connects to this server.
        var options = new DbContextOptionsBuilder<KroikoDataContext>()
            .UseSqlServer("Server=unused;Database=unused")
            .Options;
        using var context = new KroikoDataContext(options);

        context.Database.HasPendingModelChanges().Should().BeFalse();
    }
}
