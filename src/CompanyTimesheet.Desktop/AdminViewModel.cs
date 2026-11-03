using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Company.Api.Client;
using Company.Api.Contracts;

namespace CompanyTimesheet.Desktop;

/// <summary>One employee in the administrator's list</summary>
public sealed class AdminRow
{
    public AdminRow(EmployeeDto employee, CredentialDto? credential, IReadOnlyDictionary<string, string> names)
    {
        Employee = employee;
        Credential = credential;
        Code = employee.Code;
        Name = employee.Name;
        StatusText = employee.IsActive ? "Aktivan" : "Neaktivan";
        IsActive = employee.IsActive;
        SupervisorText = employee.SupervisorCode is { Length: > 0 } s ? names.GetValueOrDefault(s, s) : "—";
        UsernameText = credential == null
            ? "bez korisničkog imena"
            : credential.Username + (credential.LockedUntilUtc != null ? " (zaključan)" : credential.MustChangePassword ? " (mora promijeniti lozinku)" : "");
        RolesText = employee.IsAdmin ? "Administrator" : "";
        SearchKey = $"{employee.Code} {employee.Name} {credential?.Username}".ToLowerInvariant();
    }

    public EmployeeDto Employee { get; }

    public CredentialDto? Credential { get; }

    public string Code { get; }

    public string Name { get; }

    public string StatusText { get; }

    public bool IsActive { get; }

    public string SupervisorText { get; }

    public string UsernameText { get; }

    public string RolesText { get; }

    public bool HasUsername => Credential != null;

    public string SearchKey { get; }
}

/// <summary>The administrator's list of employees: who they report to, who has a user name, and a search</summary>
public sealed partial class AdminViewModel : ObservableObject
{
    private List<AdminRow> _all = new();

    public ObservableCollection<AdminRow> Rows { get; } = new();

    public IReadOnlyList<EmployeeDto> Employees { get; private set; } = Array.Empty<EmployeeDto>();

    [ObservableProperty] private string _filter = "";
    [ObservableProperty] private string _summaryText = "";

    public bool IsEmpty => Rows.Count == 0;

    partial void OnFilterChanged(string value) => ApplyFilter();

    public void Clear()
    {
        _all = new List<AdminRow>();
        Employees = Array.Empty<EmployeeDto>();
        Filter = "";
        SummaryText = "";
        ApplyFilter();
    }

    public async Task RefreshAsync(ICompanyAdminClient client)
    {
        var employees = await client.GetEmployeesAsync();
        var credentials = (await client.GetCredentialsAsync()).ToDictionary(c => c.EmployeeCode);
        Employees = employees;

        var names = employees.ToDictionary(e => e.Code, e => e.Name);
        _all = employees.Select(e => new AdminRow(e, credentials.GetValueOrDefault(e.Code), names)).ToList();

        var without = _all.Count(r => r.IsActive && !r.HasUsername);
        SummaryText = $"Zaposlenika: {_all.Count}. Aktivnih bez korisničkog imena: {without}.";
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var text = (Filter ?? "").Trim().ToLowerInvariant();
        Rows.Clear();
        foreach (var row in _all.Where(r => text.Length == 0 || r.SearchKey.Contains(text))) Rows.Add(row);
        OnPropertyChanged(nameof(IsEmpty));
    }

    /// <summary>The text for a refusal of the server, in Bosnian</summary>
    public static string Refusal(ApiException ex) => ex.Code switch
    {
        "chart-cycle" => "To bi napravilo krug u hijerarhiji: neko bi postao svoj vlastiti nadređeni.",
        "chart-invalid" => "Hijerarhija ne bi bila ispravna.",
        "has-reports" => "Ta osoba još ima podređene ili šihtarice koje čekaju njeno odobrenje. Prvo ih premjestite.",
        "last-admin" => "To je posljednji administrator koji se može prijaviti.",
        "unknown-person" => "Nadređeni ili zamjenik nije zaposlenik.",
        "username-taken" => "Drugi zaposlenik već ima to korisničko ime.",
        "invalid-username" => "Korisničko ime ima 3 do 50 znakova: slova, cifre, tačka, crtica ili podvlaka.",
        "weak-password" => "Lozinka mora imati najmanje 10 znakova i ne smije biti jednaka korisničkom imenu.",
        "name-required" => "Ime ne smije biti prazno.",
        "invalid-email" => "Adresa e-pošte nije ispravna.",
        "overlap" => "Već postoji zamjena za dio ovog perioda. Prvo je uklonite.",
        "invalid-period" => "Period nije ispravan: kraj je prije početka, već je prošao ili je duži od godine.",
        "invalid-deputy" => "Niko ne može biti svoja zamjena.",
        _ => ex.Message
    };
}

/// <summary>A person to choose as supervisor or designated approver; an empty code is "nobody"</summary>
public sealed record PersonOption(string Code, string Label)
{
    public override string ToString() => Label;
}

/// <summary>What the dialog of an employee asks for: the changes to the employee, and what to do with the user name</summary>
public sealed record AdminEditResult(UpdateEmployeeRequest Update, SetCredentialsRequest? SetCredentials, bool RemoveCredentials);

/// <summary>The dialog in which the administrator changes an employee</summary>
public sealed partial class AdminEmployeeViewModel : ObservableObject, IDialogResult<AdminEditResult?>
{
    private readonly AdminRow _row;

    public AdminEmployeeViewModel(AdminRow row, IReadOnlyList<EmployeeDto> everybody)
    {
        _row = row;
        var employee = row.Employee;
        Title = $"{employee.Name} · {employee.Code}";
        People = new[] { new PersonOption("", "(niko)") }
            .Concat(everybody.Where(e => e.Code != employee.Code).OrderBy(e => e.Name).Select(e => new PersonOption(e.Code, $"{e.Name} · {e.Code}"))).ToList();

        Name = employee.Name;
        IsActive = employee.IsActive;
        IsAdmin = employee.IsAdmin;
        Supervisor = People.First(p => p.Code == (employee.SupervisorCode ?? ""));
        Designated = People.FirstOrDefault(p => p.Code == (employee.DesignatedApproverCode ?? "")) ?? People[0];
        ActiveFrom = employee.ActiveFrom?.ToDateTime(TimeOnly.MinValue);
        ActiveTo = employee.ActiveTo?.ToDateTime(TimeOnly.MinValue);
        Username = row.Credential?.Username ?? "";
        HasCredentials = row.Credential != null;
        Email = employee.Email ?? "";
    }

    public string Title { get; }

    public IReadOnlyList<PersonOption> People { get; }

    public bool HasCredentials { get; }

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private bool _isAdmin;
    [ObservableProperty] private PersonOption? _supervisor;
    [ObservableProperty] private PersonOption? _designated;
    [ObservableProperty] private DateTime? _activeFrom;
    [ObservableProperty] private DateTime? _activeTo;
    [ObservableProperty] private string _email = "";
    [ObservableProperty] private string _username = "";
    [ObservableProperty] private string _newPassword = "";
    [ObservableProperty] private bool _mustChange = true;
    [ObservableProperty] private string _error = "";

    public AdminEditResult? Result { get; private set; }

    public event Action? RequestClose;

    [RelayCommand]
    private void Save()
    {
        if (string.IsNullOrWhiteSpace(Name)) { Error = "Ime ne smije biti prazno."; return; }
        if (ActiveFrom != null && ActiveTo != null && ActiveTo < ActiveFrom) { Error = "Kraj zaposlenja je prije početka."; return; }

        // A user name needs a password, and the other way round, so nobody is left with half a sign-in
        var hasName = Username.Trim().Length > 0;
        var hasPassword = NewPassword.Length > 0;
        if (hasPassword && !hasName) { Error = "Upišite korisničko ime uz lozinku."; return; }
        if (!HasCredentials && hasName && !hasPassword) { Error = "Novo korisničko ime treba prvu lozinku."; return; }
        if (hasPassword && NewPassword.Length < 10) { Error = "Lozinka mora imati najmanje 10 znakova."; return; }

        var employee = _row.Employee;
        var update = new UpdateEmployeeRequest(
            Name.Trim(), IsActive, Supervisor?.Code ?? "", null, Designated?.Code ?? "", IsAdmin,
            ActiveFrom is { } from ? DateOnly.FromDateTime(from) : null,
            ActiveTo is { } to ? DateOnly.FromDateTime(to) : null,
            ClearActiveFrom: ActiveFrom == null && employee.ActiveFrom != null,
            ClearActiveTo: ActiveTo == null && employee.ActiveTo != null,
            Email: Email.Trim());

        SetCredentialsRequest? set = null;
        if (hasName && hasPassword) set = new SetCredentialsRequest(Username.Trim(), NewPassword, MustChange);

        Result = new AdminEditResult(update, set, RemoveCredentials: false);
        RequestClose?.Invoke();
    }

    /// <summary>Takes the user name away: the person cannot sign in until the administrator gives them a new one</summary>
    [RelayCommand]
    private void RemoveUsername()
    {
        Result = new AdminEditResult(new UpdateEmployeeRequest(null, null, null, null, null, null, null, null), null, RemoveCredentials: true);
        RequestClose?.Invoke();
    }

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke();
}
