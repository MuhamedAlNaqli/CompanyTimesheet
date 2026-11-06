using System.Net;
using System.Net.Mail;
using Company.Api.Contracts;
using Company.Api.Data;
using Company.Api.Texts;
using Microsoft.EntityFrameworkCore;

namespace Company.Api.Services;

/// <summary>Sends one mail; the real one speaks SMTP, a test has its own</summary>
public interface IMailSender
{
    Task SendAsync(string to, string subject, string body, CancellationToken ct);
}

public sealed class SmtpMailSender : IMailSender
{
    private readonly IConfiguration _configuration;

    public SmtpMailSender(IConfiguration configuration) => _configuration = configuration;

    public async Task SendAsync(string to, string subject, string body, CancellationToken ct)
    {
        var host = _configuration["Smtp:Host"];
        if (string.IsNullOrWhiteSpace(host)) throw new InvalidOperationException("Smtp:Host is not set.");

        using var client = new SmtpClient(host, _configuration.GetValue("Smtp:Port", 587)) { EnableSsl = _configuration.GetValue("Smtp:UseSsl", true) };
        var user = _configuration["Smtp:User"];
        if (!string.IsNullOrWhiteSpace(user)) client.Credentials = new NetworkCredential(user, _configuration["Smtp:Password"]);

        var from = _configuration["Smtp:From"] ?? throw new InvalidOperationException("Smtp:From is not set.");
        using var message = new MailMessage(new MailAddress(from, _configuration["Smtp:FromName"] ?? "Evidencija radnog vremena"), new MailAddress(to))
        {
            Subject = subject,
            Body = body,
            SubjectEncoding = System.Text.Encoding.UTF8,
            BodyEncoding = System.Text.Encoding.UTF8
        };
        await client.SendMailAsync(message, ct);
    }
}

/// <summary>
/// The reminders of the employee mode as one mail a day, for the people who have an address (Features:EmailReminders). It says what the
/// banner and the notification say: the same rules decide, this only writes them down. A person gets at most one mail a day, on working
/// days from the hour set in Reminders:EmailHour, and only when something is due.
/// </summary>
public sealed class EmailReminderService
{
    private readonly CompanyDbContext _db;
    private readonly ReminderService _reminders;
    private readonly IMailSender _mail;
    private readonly TimeProvider _time;
    private readonly ILogger<EmailReminderService> _logger;
    private readonly int _firstHour;

    public EmailReminderService(CompanyDbContext db, ReminderService reminders, IMailSender mail, TimeProvider time, IConfiguration configuration, ILogger<EmailReminderService> logger)
    {
        _db = db;
        _reminders = reminders;
        _mail = mail;
        _time = time;
        _logger = logger;
        _firstHour = configuration.GetValue("Reminders:EmailHour", 8);
    }

    /// <summary>Whether mails go out at this moment: a working day, from the first hour, before the evening</summary>
    public bool IsMailTime(DateTime localNow) =>
        localNow.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && localNow.Hour >= _firstHour && localNow.Hour < 20;

    /// <summary>Sends today's mails that have not gone out yet. Returns how many were sent.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        var local = _time.GetLocalNow().DateTime;
        if (!IsMailTime(local)) return 0;

        var today = DateOnly.FromDateTime(local);
        var already = (await _db.ReminderMails.AsNoTracking().Where(m => m.Day == today).Select(m => m.EmployeeCode).ToListAsync(ct)).ToHashSet();
        var people = await _db.Employees.AsNoTracking().Where(e => e.IsActive && e.Email != null && e.Email != "").OrderBy(e => e.Code).ToListAsync(ct);

        var sent = 0;
        foreach (var person in people.Where(p => !already.Contains(p.Code)))
        {
            var due = await _reminders.ForAsync(person.Code);
            if (due.Items.Count == 0) continue;

            try
            {
                await _mail.SendAsync(person.Email!, ReminderTexts.Subject(due.Items, person.Language), ReminderTexts.Body(person.Name, due.Items, person.Language), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The next round tries again; one address that does not work must not stop the others
                _logger.LogWarning(ex, "The reminder mail to {Code} could not be sent", person.Code);
                continue;
            }

            _db.ReminderMails.Add(new ReminderMail { EmployeeCode = person.Code, Day = today, SentUtc = _time.GetUtcNow().UtcDateTime, Items = due.Items.Count });
            await _db.SaveChangesAsync(ct);
            sent++;
        }

        return sent;
    }
}

/// <summary>Runs <see cref="EmailReminderService"/> every ten minutes; added to the server only when the feature is on</summary>
public sealed class EmailReminderWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<EmailReminderWorker> _logger;

    public EmailReminderWorker(IServiceScopeFactory scopes, ILogger<EmailReminderWorker> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var sent = await scope.ServiceProvider.GetRequiredService<EmailReminderService>().RunOnceAsync(stoppingToken);
                if (sent > 0) _logger.LogInformation("{Count} reminder mails sent", sent);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "The reminder mails could not be run");
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
