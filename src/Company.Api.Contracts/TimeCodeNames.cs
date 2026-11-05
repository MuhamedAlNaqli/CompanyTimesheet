namespace Company.Api.Contracts;

/// <summary>
/// What the codes of the legend are called, in Bosnian (the language of the sheet) and in English. The domain library knows the codes
/// and their rules; the names are text for people and live here.
/// </summary>
public static class TimeCodeNames
{
    public static IReadOnlyDictionary<int, (string Bs, string En)> Defaults { get; } = new Dictionary<int, (string Bs, string En)>
    {
        [1010] = ("Redovan rad", "Regular work"),
        [1020] = ("Plaćeno odsustvo", "Paid leave"),
        [1021] = ("Neplaćeno odsustvo", "Unpaid leave"),
        [1022] = ("Godišnji odmor A", "Annual leave A"),
        [1023] = ("Godišnji odmor B", "Annual leave B"),
        [1024] = ("Državni praznik", "State holiday (not worked)"),
        [1025] = ("Vjerski praznik", "Religious holiday"),
        [1027] = ("Službeni put – u zemlji", "Business trip, domestic"),
        [1028] = ("Službeni put – izvan zemlje", "Business trip, abroad"),
        [1029] = ("Službeni put – bez isplate dnevnice", "Business trip without per diem"),
        [1030] = ("Bolovanje do 42 dana", "Sick leave up to 42 days"),
        [1031] = ("Bolovanje preko 42 dana", "Sick leave over 42 days"),
        [1040] = ("Porodiljsko odsustvo", "Maternity leave"),
        [2010] = ("Rad praznikom", "Work on a holiday"),
        [2011] = ("Redovan noćni rad", "Regular night work"),
        [2012] = ("Rad praznikom noćni", "Night work on a holiday"),
        [2020] = ("Prekovremeni rad", "Overtime"),
        [2021] = ("Prekovremeni noćni rad", "Night overtime"),
        [2022] = ("Prekovremeni rad praznikom", "Overtime on a holiday"),
        [2023] = ("Prekovremeni rad vikendom", "Weekend overtime"),
        [2024] = ("Prekovremeni noćni rad vikendom", "Night weekend overtime"),
        [2025] = ("Prekovremeni noćni rad praznikom", "Night holiday overtime")
    };

    private static volatile IReadOnlyDictionary<int, (string Bs, string En)> Names = Defaults;

    /// <summary>Replaces the names with the ones the server holds (the administrator can name new codes). An empty list is ignored.</summary>
    public static void Configure(IEnumerable<(int Code, string Bs, string En)> names)
    {
        var map = names.GroupBy(n => n.Code).ToDictionary(g => g.Key, g => (g.First().Bs, g.First().En));
        if (map.Count > 0) Names = map;
    }

    public static void Reset() => Names = Defaults;

    public static string Bs(int code) => Names.TryGetValue(code, out var name) ? name.Bs : code.ToString();

    public static string En(int code) => Names.TryGetValue(code, out var name) ? name.En : code.ToString();

    public static IEnumerable<int> Known => Names.Keys;
}
