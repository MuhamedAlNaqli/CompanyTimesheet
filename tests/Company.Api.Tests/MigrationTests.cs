using Company.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Company.Api.Tests;

[TestClass]
public class MigrationTests
{
    /// <summary>
    /// A change of an entity without a new migration would leave the production database behind the code; this fails until
    /// <c>dotnet ef migrations add</c> has been run. It needs no database.
    /// </summary>
    [TestMethod]
    public void TheMigrationsShouldDescribeTheCurrentModel()
    {
        using var context = new DesignTimeFactory().CreateDbContext(Array.Empty<string>());
        var assembly = context.GetService<IMigrationsAssembly>();
        Assert.IsTrue(assembly.Migrations.Count > 0, "there is at least the initial migration");

        var snapshot = assembly.ModelSnapshot!.Model;
        if (snapshot is IMutableModel mutable) snapshot = mutable.FinalizeModel();
        snapshot = context.GetService<IModelRuntimeInitializer>().Initialize(snapshot);

        var differences = context.GetService<IMigrationsModelDiffer>()
            .GetDifferences(snapshot.GetRelationalModel(), context.GetService<IDesignTimeModel>().Model.GetRelationalModel());

        Assert.AreEqual(0, differences.Count, "the model changed since the last migration: " + string.Join(", ", differences.Select(d => d.GetType().Name)));
    }
}
