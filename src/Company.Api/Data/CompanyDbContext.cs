using Microsoft.EntityFrameworkCore;

namespace Company.Api.Data;

public class CompanyDbContext : DbContext
{
    public CompanyDbContext(DbContextOptions<CompanyDbContext> options) : base(options)
    {
    }

    public DbSet<Employee> Employees => Set<Employee>();

    public DbSet<EmployeeAccount> Accounts => Set<EmployeeAccount>();

    public DbSet<StateHoliday> StateHolidays => Set<StateHoliday>();

    public DbSet<ReligiousHoliday> ReligiousHolidays => Set<ReligiousHoliday>();

    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    public DbSet<MonthSheet> Sheets => Set<MonthSheet>();

    public DbSet<SheetEntry> SheetEntries => Set<SheetEntry>();

    public DbSet<SheetOvertime> SheetOvertime => Set<SheetOvertime>();

    public DbSet<SheetChainStep> SheetChainSteps => Set<SheetChainStep>();

    public DbSet<SheetMessage> SheetMessages => Set<SheetMessage>();

    public DbSet<SheetEvent> SheetEvents => Set<SheetEvent>();

    public DbSet<SheetRevision> SheetRevisions => Set<SheetRevision>();

    public DbSet<TeamNudge> TeamNudges => Set<TeamNudge>();

    public DbSet<EmployeeCredential> Credentials => Set<EmployeeCredential>();

    public DbSet<ApproverDeputy> Deputies => Set<ApproverDeputy>();

    public DbSet<ReminderMail> ReminderMails => Set<ReminderMail>();

    public DbSet<AuthSession> AuthSessions => Set<AuthSession>();

    public DbSet<CodeDefinition> Codes => Set<CodeDefinition>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Employee>(e =>
        {
            e.ToTable("employees");
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).HasMaxLength(10).IsRequired();
            e.Property(x => x.Name).HasMaxLength(150).IsRequired();
            e.Property(x => x.SupervisorCode).HasMaxLength(10);
            e.Property(x => x.DesignatedApproverCode).HasMaxLength(10);
            e.Property(x => x.Email).HasMaxLength(200);
            e.Property(x => x.ThemeMode).HasMaxLength(10);
            e.Property(x => x.Palette).HasMaxLength(20);
            e.Property(x => x.Language).HasMaxLength(5);
            e.HasMany(x => x.Accounts).WithOne().HasForeignKey(a => a.EmployeeCode).HasPrincipalKey(x => x.Code).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CodeDefinition>(e =>
        {
            e.ToTable("code_definitions");
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.NameBs).HasMaxLength(150).IsRequired();
            e.Property(x => x.NameEn).HasMaxLength(150).IsRequired();
            e.Property(x => x.Colour).HasMaxLength(10).IsRequired();
            e.Property(x => x.Unit).HasMaxLength(10).IsRequired();
        });

        modelBuilder.Entity<EmployeeAccount>(e =>
        {
            e.ToTable("employee_accounts");
            e.HasIndex(x => x.Account).IsUnique();
            e.Property(x => x.Account).HasMaxLength(256).IsRequired();
            e.Property(x => x.EmployeeCode).HasMaxLength(10).IsRequired();
        });

        modelBuilder.Entity<StateHoliday>(e =>
        {
            e.ToTable("state_holidays");
            e.HasIndex(x => x.Date).IsUnique();
            e.Property(x => x.Name).HasMaxLength(150).IsRequired();
        });

        modelBuilder.Entity<ReligiousHoliday>(e =>
        {
            e.ToTable("religious_holidays");
            e.HasIndex(x => new { x.EmployeeCode, x.Date }).IsUnique();
            e.Property(x => x.EmployeeCode).HasMaxLength(10).IsRequired();
            e.Property(x => x.Name).HasMaxLength(150).IsRequired();
        });

        modelBuilder.Entity<MonthSheet>(e =>
        {
            e.ToTable("month_sheets");
            e.HasIndex(x => new { x.EmployeeCode, x.Year, x.Month }).IsUnique();
            e.Property(x => x.EmployeeCode).HasMaxLength(10).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.HasMany(x => x.Entries).WithOne().HasForeignKey(x => x.SheetId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Overtime).WithOne().HasForeignKey(x => x.SheetId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Messages).WithOne().HasForeignKey(x => x.SheetId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Events).WithOne().HasForeignKey(x => x.SheetId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.ChainSteps).WithOne().HasForeignKey(x => x.SheetId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeCode).HasPrincipalKey(x => x.Code).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.Property(x => x.CurrentApproverCode).HasMaxLength(10);
            e.HasIndex(x => new { x.CurrentApproverCode, x.Status });
            e.Ignore(x => x.ChainCodes);
        });

        modelBuilder.Entity<SheetChainStep>(e =>
        {
            e.ToTable("sheet_chain_steps");
            e.HasIndex(x => new { x.SheetId, x.Position }).IsUnique();
            e.Property(x => x.EmployeeCode).HasMaxLength(10).IsRequired();
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeCode).HasPrincipalKey(x => x.Code).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SheetMessage>(e =>
        {
            e.ToTable("sheet_messages");
            e.HasIndex(x => new { x.SheetId, x.Position }).IsUnique();
            e.Property(x => x.FromCode).HasMaxLength(10).IsRequired();
            e.Property(x => x.Text).HasMaxLength(2000).IsRequired();
            e.Property(x => x.Answer).HasMaxLength(2000);
        });

        modelBuilder.Entity<SheetEvent>(e =>
        {
            e.ToTable("sheet_events");
            e.HasIndex(x => x.SheetId);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.ActorCode).HasMaxLength(10).IsRequired();
            e.Property(x => x.OnBehalfOf).HasMaxLength(10);
            e.Property(x => x.Text).HasMaxLength(2000);
        });

        modelBuilder.Entity<EmployeeCredential>(e =>
        {
            e.ToTable("employee_credentials");
            e.HasIndex(x => x.EmployeeCode).IsUnique();
            e.HasIndex(x => x.Username).IsUnique();
            e.Property(x => x.EmployeeCode).HasMaxLength(10).IsRequired();
            e.Property(x => x.Username).HasMaxLength(50).IsRequired();
            e.Property(x => x.PasswordHash).HasMaxLength(300).IsRequired();
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeCode).HasPrincipalKey(x => x.Code).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ReminderMail>(e =>
        {
            e.ToTable("reminder_mails");
            e.HasIndex(x => new { x.EmployeeCode, x.Day }).IsUnique();
            e.Property(x => x.EmployeeCode).HasMaxLength(10).IsRequired();
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeCode).HasPrincipalKey(x => x.Code).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ApproverDeputy>(e =>
        {
            e.ToTable("approver_deputies");
            e.HasIndex(x => new { x.DeputyCode, x.FromDate, x.ToDate });
            e.HasIndex(x => new { x.ApproverCode, x.FromDate });
            e.Property(x => x.ApproverCode).HasMaxLength(10).IsRequired();
            e.Property(x => x.DeputyCode).HasMaxLength(10).IsRequired();
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.ApproverCode).HasPrincipalKey(x => x.Code).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.DeputyCode).HasPrincipalKey(x => x.Code).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AuthSession>(e =>
        {
            e.ToTable("auth_sessions");
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => x.EmployeeCode);
            e.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
            e.Property(x => x.EmployeeCode).HasMaxLength(10).IsRequired();
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeCode).HasPrincipalKey(x => x.Code).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TeamNudge>(e =>
        {
            e.ToTable("team_nudges");
            e.HasIndex(x => new { x.EmployeeCode, x.Year, x.Month });
            e.Property(x => x.EmployeeCode).HasMaxLength(10).IsRequired();
            e.Property(x => x.FromCode).HasMaxLength(10).IsRequired();
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeCode).HasPrincipalKey(x => x.Code).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.FromCode).HasPrincipalKey(x => x.Code).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SheetRevision>(e =>
        {
            e.ToTable("sheet_revisions");
            e.HasIndex(x => new { x.SheetId, x.Revision }).IsUnique();
            e.HasOne<MonthSheet>().WithMany().HasForeignKey(x => x.SheetId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SheetEntry>(e =>
        {
            e.ToTable("sheet_entries");
            e.HasIndex(x => new { x.SheetId, x.Date, x.Code }).IsUnique();
        });

        modelBuilder.Entity<SheetOvertime>(e =>
        {
            e.ToTable("sheet_overtime");
            e.HasIndex(x => new { x.SheetId, x.Date });
            e.Property(x => x.Description).HasMaxLength(500).IsRequired();
        });

        modelBuilder.Entity<AuditEvent>(e =>
        {
            e.ToTable("audit_events");
            e.HasIndex(x => x.AtUtc);
            e.Property(x => x.Actor).HasMaxLength(256).IsRequired();
            e.Property(x => x.Action).HasMaxLength(100).IsRequired();
            e.Property(x => x.EntityType).HasMaxLength(100).IsRequired();
            e.Property(x => x.EntityId).HasMaxLength(100).IsRequired();
            e.Property(x => x.Details).HasMaxLength(2000);
            e.Property(x => x.Hash).HasMaxLength(64);
            e.Property(x => x.PreviousHash).HasMaxLength(64);

            // Two events cannot follow the same event: a second server writing at the same moment is stopped instead of forking the chain
            e.HasIndex(x => x.PreviousHash).IsUnique();
        });
    }

    private static readonly SemaphoreSlim AuditLock = new(1, 1);

    /// <summary>
    /// The audit log is append-only: changing or deleting an event is refused in the application itself. Every event is chained to
    /// the one before it by a hash (see <see cref="AuditChain"/>), so a change made behind the application's back shows up in the
    /// verification. Every change to a sheet or to anything that belongs to it also counts the sheet's version, here, so that no
    /// code path can change a sheet and forget to.
    /// </summary>
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        GuardAudit();
        CountSheetVersions();

        var added = AddedAuditEvents();
        if (added.Count == 0) return base.SaveChanges(acceptAllChangesOnSuccess);

        AuditLock.Wait();
        try
        {
            var last = AuditEvents.AsNoTracking().OrderByDescending(e => e.Id).Select(e => e.Hash).FirstOrDefault();
            if (added.Count == 1)
            {
                AuditChain.Link(added, last);
                return base.SaveChanges(acceptAllChangesOnSuccess);
            }

            // Several events in one save: the database may give them their numbers in another order than the one they were chained in, which
            // would break the chain. So they are written one at a time, in the order they are chained, all in one transaction.
            var own = Database.CurrentTransaction == null ? Database.BeginTransaction() : null;
            try
            {
                foreach (var e in added.Skip(1)) Entry(e).State = EntityState.Detached;

                AuditChain.Link(added.Take(1).ToList(), last);
                var saved = base.SaveChanges(acceptAllChangesOnSuccess);
                last = added[0].Hash;
                foreach (var e in added.Skip(1))
                {
                    Entry(e).State = EntityState.Added;
                    AuditChain.Link(new List<AuditEvent> { e }, last);
                    saved += base.SaveChanges(acceptAllChangesOnSuccess);
                    last = e.Hash;
                }

                own?.Commit();
                return saved;
            }
            finally
            {
                own?.Dispose();
            }
        }
        finally
        {
            AuditLock.Release();
        }
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        GuardAudit();
        CountSheetVersions();

        var added = AddedAuditEvents();
        if (added.Count == 0) return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);

        await AuditLock.WaitAsync(cancellationToken);
        try
        {
            var last = await AuditEvents.AsNoTracking().OrderByDescending(e => e.Id).Select(e => e.Hash).FirstOrDefaultAsync(cancellationToken);
            if (added.Count == 1)
            {
                AuditChain.Link(added, last);
                return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
            }

            // See SaveChanges: several events are written one at a time, in the order they are chained, in one transaction
            var own = Database.CurrentTransaction == null ? await Database.BeginTransactionAsync(cancellationToken) : null;
            try
            {
                foreach (var e in added.Skip(1)) Entry(e).State = EntityState.Detached;

                AuditChain.Link(added.Take(1).ToList(), last);
                var saved = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
                last = added[0].Hash;
                foreach (var e in added.Skip(1))
                {
                    Entry(e).State = EntityState.Added;
                    AuditChain.Link(new List<AuditEvent> { e }, last);
                    saved += await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
                    last = e.Hash;
                }

                if (own != null) await own.CommitAsync(cancellationToken);
                return saved;
            }
            finally
            {
                if (own != null) await own.DisposeAsync();
            }
        }
        finally
        {
            AuditLock.Release();
        }
    }

    private List<AuditEvent> AddedAuditEvents() =>
        ChangeTracker.Entries<AuditEvent>().Where(e => e.State == EntityState.Added).Select(e => e.Entity).ToList();

    private void GuardAudit()
    {
        if (ChangeTracker.Entries<AuditEvent>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("The audit log is append-only.");
    }

    /// <summary>A sheet whose own row or anything of it (entries, overtime, messages, events, chain) changes gets the next version once per save</summary>
    private void CountSheetVersions()
    {
        ChangeTracker.DetectChanges();

        var touched = new HashSet<int>();
        foreach (var entry in ChangeTracker.Entries().Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            var sheetId = entry.Entity switch
            {
                SheetEntry x => x.SheetId,
                SheetOvertime x => x.SheetId,
                SheetMessage x => x.SheetId,
                SheetEvent x => x.SheetId,
                SheetChainStep x => x.SheetId,
                _ => 0
            };
            if (sheetId > 0) touched.Add(sheetId);
        }

        foreach (var entry in ChangeTracker.Entries<MonthSheet>().Where(e => e.State != EntityState.Added && e.State != EntityState.Deleted))
        {
            if (entry.State != EntityState.Modified && !touched.Contains(entry.Entity.Id)) continue;

            var version = entry.Property(x => x.Version);
            if (version.CurrentValue == version.OriginalValue) version.CurrentValue = version.OriginalValue + 1;
        }
    }
}
