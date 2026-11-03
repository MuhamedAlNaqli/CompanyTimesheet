using System.Net;
using System.Net.Http.Json;
using Company.Api.Contracts;
using Company.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Company.Api.Tests;

/// <summary>The reminders as one mail a day (Features:EmailReminders), written by the same rules as the notification</summary>
[TestClass]
public class EmailReminderTests
{
    private sealed class FakeMail : IMailSender
    {
        public List<(string To, string Subject, string Body)> Sent { get; } = new();
        public HashSet<string> Failing { get; } = new();

        public Task SendAsync(string to, string subject, string body, CancellationToken ct)
        {
            if (Failing.Contains(to)) throw new InvalidOperationException("the mail server refused");
            Sent.Add((to, subject, body));
            return Task.CompletedTask;
        }
    }

    private sealed class MailFactory : ApiFactory
    {
        private readonly FakeMail _mail;

        public MailFactory(FakeMail mail, Dictionary<string, string?> settings) : base(settings) => _mail = mail;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services => services.Replace(ServiceDescriptor.Singleton<IMailSender>(_mail)));
        }
    }

    /// <summary>Amra and Damir report to Benjamin; the clock shows a Monday morning in the middle of October, so September is overdue</summary>
    private static async Task<(MailFactory Factory, FakeMail Mail, MovableTime Time)> StartAsync(Dictionary<string, string?>? settings = null)
    {
        var time = new MovableTime(2026, 10, 12);
        var mail = new FakeMail();
        var all = new Dictionary<string, string?> { ["Reminders:StartFrom"] = "2026-09" };
        if (settings != null) foreach (var pair in settings) all[pair.Key] = pair.Value;
        var factory = new MailFactory(mail, all) { Time = time };
        await factory.ImportAsync(ApiFactory.SmallList);
        foreach (var (code, account) in new[] { ("Z-001", @"corp\amra"), ("Z-002", @"corp\benjamin"), ("Z-003", @"corp\emir"), ("Z-004", @"corp\damir") })
            await factory.LinkAsync(code, account);
        await factory.UpdateAsync("Z-001", new(null, null, "Z-002", null, null, null, null, null, Email: "amra@firma.ba"));
        await factory.UpdateAsync("Z-004", new(null, null, "Z-002", null, null, null, null, null, Email: "damir@firma.ba"));
        await factory.UpdateAsync("Z-002", new(null, null, "Z-003", null, null, null, null, null));
        return (factory, mail, time);
    }

    private static async Task<int> Run(ApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<EmailReminderService>().RunOnceAsync();
    }

    [TestMethod]
    public async Task APersonWithAnAddressAndSomethingDue_GetsOneMail_InBosnian()
    {
        var (factory, mail, _) = await StartAsync();
        using (factory)
        {
            Assert.AreEqual(2, await Run(factory), "Amra and Damir have an address and an overdue month; Benjamin has no address");

            var amra = mail.Sent.Single(m => m.To == "amra@firma.ba");
            StringAssert.Contains(amra.Subject, "kasni");
            StringAssert.Contains(amra.Body, "Poštovani/a Amra Adžamija");
            StringAssert.Contains(amra.Body, "Šihtarica za Septembar 2026. je trebala biti predana do");
        }
    }

    [TestMethod]
    public async Task ThePersonIsMailedOnceADay_AndAgainTheNextWorkingDay()
    {
        var (factory, mail, time) = await StartAsync();
        using (factory)
        {
            await Run(factory);
            Assert.AreEqual(0, await Run(factory), "the same day: nothing more");
            Assert.AreEqual(2, mail.Sent.Count);

            time.Now = time.Now.AddDays(1);
            Assert.AreEqual(2, await Run(factory), "the next day, while it is still due");
        }
    }

    [TestMethod]
    public async Task NothingIsSent_AtNight_OnAWeekend_OrWhenNothingIsDue()
    {
        var (factory, mail, time) = await StartAsync();
        using (factory)
        {
            time.Now = new DateTimeOffset(2026, 10, 12, 6, 0, 0, TimeSpan.Zero);
            Assert.AreEqual(0, await Run(factory), "before the first hour");
            time.Now = new DateTimeOffset(2026, 10, 12, 21, 0, 0, TimeSpan.Zero);
            Assert.AreEqual(0, await Run(factory), "in the evening");
            time.Now = new DateTimeOffset(2026, 10, 17, 9, 0, 0, TimeSpan.Zero);
            Assert.AreEqual(0, await Run(factory), "on a Saturday");
            Assert.AreEqual(0, mail.Sent.Count);

            // Amra submits; her mail stops, Damir's stays
            time.Now = new DateTimeOffset(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);
            var amra = factory.As(@"corp\amra");
            await amra.PostAsync("/api/timesheet/2026/9/submit", null);
            await amra.PostAsync("/api/timesheet/2026/10/submit", null);
            Assert.AreEqual(1, await Run(factory));
            Assert.AreEqual("damir@firma.ba", mail.Sent.Single().To);
        }
    }

    [TestMethod]
    public async Task AFailingAddress_DoesNotStopTheOthers_AndIsTriedAgainLater()
    {
        var (factory, mail, _) = await StartAsync();
        using (factory)
        {
            mail.Failing.Add("amra@firma.ba");
            Assert.AreEqual(1, await Run(factory));
            Assert.AreEqual("damir@firma.ba", mail.Sent.Single().To);

            mail.Failing.Clear();
            Assert.AreEqual(1, await Run(factory), "the next round tries the one that failed");
            Assert.AreEqual("amra@firma.ba", mail.Sent.Last().To);
        }
    }

    [TestMethod]
    public async Task ASupervisorsReminder_IsInTheMailToo()
    {
        var (factory, mail, _) = await StartAsync();
        using (factory)
        {
            await factory.UpdateAsync("Z-002", new(null, null, null, null, null, null, null, null, Email: "benjamin@firma.ba"));
            await factory.As(@"corp\benjamin").PostAsync("/api/team/2026/10/remind/Z-001", null);

            await Run(factory);

            var amra = mail.Sent.Single(m => m.To == "amra@firma.ba");
            StringAssert.Contains(amra.Body, "Benjamin Bašić vas podsjeća da predate šihtaricu za Oktobar 2026.");
        }
    }

    [TestMethod]
    public async Task TheAddress_IsCheckedAndCanBeCleared()
    {
        var (factory, _, _) = await StartAsync();
        using (factory)
        {
            var bad = await factory.UpdateAsync("Z-001", new(null, null, null, null, null, null, null, null, Email: "not an address"));
            Assert.AreEqual(HttpStatusCode.BadRequest, bad.StatusCode);
            Assert.AreEqual("invalid-email", (await bad.ReadAsync<ApiErrorDto>())!.Code);

            var cleared = await factory.UpdateAsync("Z-001", new(null, null, null, null, null, null, null, null, Email: ""));
            Assert.AreEqual(HttpStatusCode.OK, cleared.StatusCode);
            Assert.IsNull((await factory.Admin.GetFromJsonAsync<EmployeeDto>("/api/admin/employees/Z-001"))!.Email);
        }
    }

    [TestMethod]
    public async Task TheWorker_RunsOnlyWhenTheFeatureIsOn()
    {
        var (off, _, _) = await StartAsync();
        using (off)
            Assert.IsFalse(off.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>().Any(s => s is EmailReminderWorker), "off by default");

        var (on, _, _) = await StartAsync(new Dictionary<string, string?> { ["Features:EmailReminders"] = "true" });
        using (on)
            Assert.IsTrue(on.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>().Any(s => s is EmailReminderWorker));
    }
}
