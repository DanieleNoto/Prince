using System;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

public partial class PrinceProcessListPanel
{
    public bool IsEditingFilter => editing >= 0;
    const float FilterHeight = .23f;
    readonly string[] filterText = { "", "", "" };
    readonly string[] filterNames = { "Cerca", "Owner", "Stato" };
    readonly TMP_Text[] filterLabels = new TMP_Text[3];
    TMP_Text periodLabel;
    GameObject filterKeyboard;
    int editing = -1, period;
#if ENABLE_INPUT_SYSTEM
    UnityEngine.InputSystem.Keyboard physicalKeyboard;
#endif

    void Update()
    {
#if ENABLE_INPUT_SYSTEM
        var current = UnityEngine.InputSystem.Keyboard.current;
        if (current != physicalKeyboard)
        {
            if (physicalKeyboard != null) physicalKeyboard.onTextInput -= TypeCharacter;
            physicalKeyboard = current;
            if (physicalKeyboard != null) physicalKeyboard.onTextInput += TypeCharacter;
        }
#elif ENABLE_LEGACY_INPUT_MANAGER
        foreach (char c in Input.inputString) TypeCharacter(c);
#endif
    }

    void OnDisable()
    {
#if ENABLE_INPUT_SYSTEM
        if (physicalKeyboard != null) physicalKeyboard.onTextInput -= TypeCharacter;
        physicalKeyboard = null;
#endif
        FinishEditing();
    }

    void TypeCharacter(char c)
    {
        if (editing < 0 || !Application.isFocused) return;
        if (c == '\n' || c == '\r' || c == (char)27) { FinishEditing(); return; }
        if (c == '\b')
        {
            if (filterText[editing].Length > 0) filterText[editing] = filterText[editing].Substring(0, filterText[editing].Length - 1);
        }
        else if (!char.IsControl(c) && filterText[editing].Length < 100) filterText[editing] += c;
        ApplyFilters(); RefreshFilterLabels();
    }

    void FinishEditing()
    {
        editing = -1;
        if (filterKeyboard != null) filterKeyboard.SetActive(false);
        RefreshFilterLabels();
    }

    void RefreshFilterLabels()
    {
        for (int i = 0; i < 3; i++) if (filterLabels[i] != null)
            filterLabels[i].text = filterNames[i] + ": " + (filterText[i].Length == 0 ? "Tutti" : filterText[i]) + (editing == i ? " |" : "");
        if (periodLabel != null) periodLabel.text = new[] { "Periodo: tutto", "Oggi", "Ultimi 7 giorni", "Ultimi 30 giorni" }[period];
    }

    static bool ContainsText(string value, string query) =>
        (value ?? "").IndexOf(query.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;

    void ApplyFilters(bool redraw = true)
    {
        filtered.Clear();
        DateTimeOffset now = DateTimeOffset.Now;
        DateTimeOffset since = period == 1 ? new DateTimeOffset(now.Date, now.Offset)
            : period == 2 ? now.AddDays(-7) : now.AddDays(-30);
        foreach (var p in processes)
        {
            string owner = string.IsNullOrWhiteSpace(p.user) ? "Senza owner" : p.user;
            if (!ContainsText(owner, filterText[1]) || !ContainsText(p.status, filterText[2])) continue;
            string searchable = string.Join(" ", p.DisplayName, p.id, p.processId, p.filename, owner, p.status, p.uploaded,
                p.version.ToString(CultureInfo.InvariantCulture), p.Latest?.id, p.Latest?.key, p.Latest?.versionTag);
            if (!ContainsText(searchable, filterText[0])) continue;
            if (period != 0)
            {
                if (!DateTimeOffset.TryParse(p.uploaded, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var date)
                    || date < since || date > now) continue;
            }
            filtered.Add(p);
        }
        page = 0;
        if (redraw) ShowPage();
    }

    TMP_Text FilterButton(Vector3 position, Vector2 size, string text, UnityAction action, bool key = false)
    {
        Button(position, size, text, action);
        Transform button = transform.GetChild(transform.childCount - 1);
        if (key) button.SetParent(filterKeyboard.transform, false);
        return button.GetComponentInChildren<TMP_Text>();
    }

    void BuildFilters(float top, float inner, float bottom)
    {
        for (int i = 0; i < 3; i++)
        {
            int field = i;
            filterLabels[i] = FilterButton(new Vector3(0, top - .027f - i * .054f, -.009f), new Vector2(inner, .047f), "", () =>
            {
                editing = field; filterKeyboard.SetActive(true); RefreshFilterLabels();
            });
        }
        periodLabel = FilterButton(new Vector3(-inner*.22f, top-.191f, -.009f), new Vector2(inner*.55f,.047f), "", () =>
        { period = (period + 1) % 4; ApplyFilters(); RefreshFilterLabels(); });
        FilterButton(new Vector3(inner*.3f, top-.191f,-.009f), new Vector2(inner*.38f,.047f), "Azzera filtri", () =>
        { for (int i=0;i<3;i++)filterText[i]="";period=0;FinishEditing();ApplyFilters(); });
        filterKeyboard = new GameObject("Tastiera filtri"); filterKeyboard.transform.SetParent(transform,false);
        string[] keys = { "1234567890", "qwertyuiop", "asdfghjkl", "zxcvbnm_-" };
        float step = inner / 10f;
        for(int row=0;row<keys.Length;row++) for(int col=0;col<keys[row].Length;col++)
        {
            char character=keys[row][col];
            FilterButton(new Vector3((col-4.5f)*step,bottom-.05f-row*.052f,-.009f), new Vector2(step-.005f,.046f), character.ToString(), ()=>TypeCharacter(character),true);
        }
        FilterButton(new Vector3(-inner*.33f,bottom-.266f,-.009f),new Vector2(inner*.3f,.048f),"Spazio",()=>TypeCharacter(' '),true);
        FilterButton(new Vector3(0,bottom-.266f,-.009f),new Vector2(inner*.3f,.048f),"Cancella",()=>TypeCharacter('\b'),true);
        FilterButton(new Vector3(inner*.33f,bottom-.266f,-.009f),new Vector2(inner*.3f,.048f),"Fine",FinishEditing,true);
        filterKeyboard.SetActive(false); RefreshFilterLabels();
    }
}
