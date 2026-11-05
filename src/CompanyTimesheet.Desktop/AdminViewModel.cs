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

/// <summary>One box of the chart of who reports to whom, with the boxes of the people below it</summary>
public sealed partial class OrgNode : ObservableObject
{
    private readonly Action<string, bool>? _expandedChanged;

    public OrgNode(AdminRow row, bool hasParent, bool isFirst, bool isLast, bool expanded, Action<string, bool>? expandedChanged)
    {
        Row = row;
        HasParent = hasParent;
        IsFirst = isFirst;
        IsLast = isLast;
        _isExpanded = expanded;
        _expandedChanged = expandedChanged;
    }

    public AdminRow Row { get; }

    public string Name => Row.Name;

    public string Code => Row.Code;

    public bool IsActive => Row.IsActive;

    public bool IsAdmin => Row.Employee.IsAdmin;

    /// <summary>The line from the box above is drawn (everybody but the top of the chart)</summary>
    public bool HasParent { get; }

    /// <summary>The first and the last of the boxes in a row stop the horizontal line at their own middle</summary>
    public bool IsFirst { get; }

    public bool IsLast { get; }

    public ObservableCollection<OrgNode> Children { get; } = new();

    /// <summary>Everybody below, to any depth</summary>
    public int Descendants { get; set; }

    public bool HasChildren => Children.Count > 0;

    public bool ShowChildren => HasChildren && IsExpanded;

    public string Caption => Descendants == 0 ? Code : $"{Code} · podređenih: {Descendants}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowChildren))]
    private bool _isExpanded;

    partial void OnIsExpandedChanged(bool value) => _expandedChanged?.Invoke(Code, value);

    [RelayCommand]
    private void Toggle() => IsExpanded = !IsExpanded;
}

/// <summary>The administrator's list of employees: who they report to, who has a user name, a search, and the chart of the hierarchy</summary>
public sealed partial class AdminViewModel : ObservableObject
{
    private List<AdminRow> _all = new();
    private readonly Dictionary<string, bool> _expanded = new();

    public ObservableCollection<AdminRow> Rows { get; } = new();

    /// <summary>The top of the hierarchy: everybody who reports to nobody, each with the people below as boxes</summary>
    public ObservableCollection<OrgNode> Chart { get; } = new();

    /// <summary>The hierarchy as a tree of boxes instead of the table</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowList))]
    private bool _showChart;

    /// <summary>The codes of the legend instead of the people</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowList), nameof(ShowPeople))]
    private bool _showCodes;

    public bool ShowList => !ShowChart && !ShowCodes;

    /// <summary>The people (table or chart), not the codes</summary>
    public bool ShowPeople => !ShowCodes;

    public ObservableCollection<CodeRow> Codes { get; } = new();

    [ObservableProperty] private string _codesSummary = "";

    [RelayCommand]
    private void ShowListView()
    {
        ShowCodes = false;
        ShowChart = false;
    }

    [RelayCommand]
    private void ShowChartView()
    {
        ShowCodes = false;
        ShowChart = true;
    }

    [RelayCommand]
    private void ShowCodesView()
    {
        ShowChart = false;
        ShowCodes = true;
    }

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
        Chart.Clear();
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
        BuildChart();

        var codes = await client.GetCodesAsync();
        Codes.Clear();
        foreach (var code in codes.OrderBy(c => c.Code)) Codes.Add(new CodeRow(code));
        CodesSummary = $"Šifara: {Codes.Count}, isključenih: {Codes.Count(c => !c.IsActive)}. Nova šifra odmah postaje dostupna svim zaposlenicima, bez nove verzije programa.";
    }

    /// <summary>
    /// The tree of who reports to whom. A person whose supervisor is nobody (or is not in the list) is the top of a branch. In a big
    /// company only the first two levels are open at the start; a box the administrator opens or closes stays so after a refresh.
    /// </summary>
    private void BuildChart()
    {
        Chart.Clear();

        var known = _all.Select(r => r.Code).ToHashSet();
        string? SupervisorOf(AdminRow r) => r.Employee.SupervisorCode is { Length: > 0 } s && s != r.Code && known.Contains(s) ? s : null;

        var below = _all.Where(r => SupervisorOf(r) != null).GroupBy(r => SupervisorOf(r)!)
            .ToDictionary(g => g.Key, g => g.OrderBy(r => r.Name).ToList());
        var visited = new HashSet<string>();
        var openAll = _all.Count <= 60;

        OrgNode Build(AdminRow row, int depth, bool hasParent, bool first, bool last)
        {
            visited.Add(row.Code);
            var open = _expanded.TryGetValue(row.Code, out var chosen) ? chosen : openAll || depth < 2;
            var node = new OrgNode(row, hasParent, first, last, open, (code, value) => _expanded[code] = value);

            var kids = below.TryGetValue(row.Code, out var list) ? list.Where(k => !visited.Contains(k.Code)).ToList() : new List<AdminRow>();
            for (var i = 0; i < kids.Count; i++) node.Children.Add(Build(kids[i], depth + 1, true, i == 0, i == kids.Count - 1));
            node.Descendants = node.Children.Sum(c => 1 + c.Descendants);
            return node;
        }

        foreach (var root in _all.Where(r => SupervisorOf(r) == null).OrderBy(r => r.Name))
            Chart.Add(Build(root, 0, false, true, true));

        // Anybody left out (a loop that the server should never allow) still shows, as a top of its own
        foreach (var left in _all.Where(r => !visited.Contains(r.Code)).OrderBy(r => r.Name).ToList())
            if (!visited.Contains(left.Code)) Chart.Add(Build(left, 0, false, true, true));
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
        "code-taken" => "Ta šifra već pripada drugoj osobi.",
        "invalid-code" => "Šifra može imati slova, cifre, crticu, tačku ili podvlaku (najviše 10 znakova).",
        "invalid-email" => "Adresa e-pošte nije ispravna.",
        "overlap" => "Već postoji zamjena za dio ovog perioda. Prvo je uklonite.",
        "invalid-period" => "Period nije ispravan: kraj je prije početka, već je prošao ili je duži od godine.",
        "invalid-deputy" => "Niko ne može biti svoja zamjena.",
        "code-exists" => "Ta šifra već postoji.",
        "system-code" => "Program koristi tu šifru u svojim pravilima: može se samo preimenovati.",
        "code-in-use" => "Šihtarice već koriste tu šifru, pa joj se boja i vrsta unosa ne mogu mijenjati. Isključite je i dodajte novu.",
        "overtime-code" => "Šifre za prekovremeni rad dolaze s programom: prekovremeni se računa iz intervala.",
        "invalid-kind" => "Boja ili vrsta unosa nisu ispravni.",
        _ => ex.Message
    };
}

/// <summary>A person to choose as supervisor or designated approver; an empty code is "nobody"</summary>
public sealed record PersonOption(string Code, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// What the dialog of an employee asks for: the changes to the employee, and what to do with the user name. For a new person Create is
/// set (and Update is empty): the person is added first, then the user name is given to the code the server chose.
/// </summary>
public sealed record AdminEditResult(UpdateEmployeeRequest Update, SetCredentialsRequest? SetCredentials, bool RemoveCredentials, CreateEmployeeRequest? Create = null);

/// <summary>The dialog in which the administrator changes an employee, or adds a new one (no row)</summary>
public sealed partial class AdminEmployeeViewModel : ObservableObject, IDialogResult<AdminEditResult?>
{
    private readonly AdminRow? _row;

    public AdminEmployeeViewModel(AdminRow? row, IReadOnlyList<EmployeeDto> everybody)
    {
        _row = row;
        var employee = row?.Employee ?? new EmployeeDto("", "", true, null, false, null, false, null, null, Array.Empty<string>());
        IsNew = row == null;
        Title = IsNew ? "Nova osoba" : $"{employee.Name} · {employee.Code}";
        People = new[] { new PersonOption("", "(niko)") }
            .Concat(everybody.Where(e => e.Code != employee.Code).OrderBy(e => e.Name).Select(e => new PersonOption(e.Code, $"{e.Name} · {e.Code}"))).ToList();

        Name = employee.Name;
        IsActive = employee.IsActive;
        IsAdmin = employee.IsAdmin;
        Supervisor = People.First(p => p.Code == (employee.SupervisorCode ?? ""));
        Designated = People.FirstOrDefault(p => p.Code == (employee.DesignatedApproverCode ?? "")) ?? People[0];
        ActiveFrom = employee.ActiveFrom?.ToDateTime(TimeOnly.MinValue);
        ActiveTo = employee.ActiveTo?.ToDateTime(TimeOnly.MinValue);
        Username = row?.Credential?.Username ?? "";
        HasCredentials = row?.Credential != null;
        Email = employee.Email ?? "";
    }

    public string Title { get; }

    /// <summary>A person who does not exist yet: the code can be chosen, and "active" and the designated approver are left out</summary>
    public bool IsNew { get; }

    public bool IsExisting => !IsNew;

    /// <summary>Left empty, the server gives the next free code (Z-nnn)</summary>
    [ObservableProperty] private string _code = "";

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

        if (IsNew)
        {
            var create = new CreateEmployeeRequest(
                Code.Trim(), Name.Trim(), Supervisor?.Code, Email.Trim(), IsAdmin,
                ActiveFrom is { } begin ? DateOnly.FromDateTime(begin) : null,
                ActiveTo is { } end ? DateOnly.FromDateTime(end) : null);
            Result = new AdminEditResult(new UpdateEmployeeRequest(null, null, null, null, null, null, null, null),
                hasName && hasPassword ? new SetCredentialsRequest(Username.Trim(), NewPassword, MustChange) : null, RemoveCredentials: false, create);
            RequestClose?.Invoke();
            return;
        }

        var employee = _row!.Employee;
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
