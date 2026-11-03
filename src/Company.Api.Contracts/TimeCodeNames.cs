namespace Company.Api.Contracts;

/// <summary>
/// What the codes of the legend are called, in Bosnian (the language of the sheet) and in English. The domain library knows the codes
/// and their rules; the names are text for people and live here.
/// </summary>
public static class TimeCodeNames
{
    private static readonly Dictionary<int, (string Bs, string En)> Names = new()
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

    public static string Bs(int code) => Names.TryGetValue(code, out var name) ? name.Bs : code.ToString();

    public static string En(int code) => Names.TryGetValue(code, out var name) ? name.En : code.ToString();

    public static IEnumerable<int> Known => Names.Keys;
}
