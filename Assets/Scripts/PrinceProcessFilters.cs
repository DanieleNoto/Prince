using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Filtri della lista processi, sul modello della pagina web Prince:
/// ricerca testuale (nome, ID, file, owner), Stato, Owner, intervallo di date con accesso rapido
/// (Mese, Settimana, Giorno), opzioni (Nascondi deprecati, Di tutti gli owner, Tutte le versioni),
/// ordinamento e Azzera filtri. Preferiti e fissati non sono gestiti: il server non li espone.
/// </summary>
public partial class PrinceProcessListPanel
{
    const float FilterRowHeight = .047f;
    const float FilterStep = .054f;
    const int FilterRows = 7;
    const float FilterHeight = FilterRows * FilterStep + .012f;
    const float FilterGap = .01f;
    const int PickerColumns = 3, PickerRowsPerPage = 4;

    public bool IsEditingFilter => editing >= 0;

    static readonly string[] SortNames = { "ID", "Nome", "Proprietario", "Versione", "Caricato il", "Stato" };

    // Stato dei filtri. "Di tutti gli owner" parte acceso, le altre opzioni spente (come il web).
    string searchText = "", statusFilter = "", ownerFilter = "";
    DateTime? dateFrom, dateTo;
    bool hideDeprecated, allOwners = true, allVersions;
    int sortField = 4;
    bool sortDescending = true;

    TMP_Text searchLabel, statusLabel, ownerLabel, fromLabel, toLabel;
    TMP_Text deprecatedLabel, ownersLabel, versionsLabel, sortLabel, sortDirectionLabel;
    GameObject filterKeyboard, filterPopup;
    int editing = -1; // 0 = testo di ricerca in modifica
    float filterInner, filterBottom;

    // Selettore a griglia (Stato / Owner)
    List<string> pickerOptions;
    Action<string> pickerChoose;
    string pickerTitle;
    int pickerPage;

#if ENABLE_INPUT_SYSTEM
    UnityEngine.InputSystem.Keyboard physicalKeyboard;
#endif

    // ---------- Tastiera fisica (Editor) ----------

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
        ClosePopup();
    }

    void TypeCharacter(char c)
    {
        if (editing < 0 || !Application.isFocused) return;
        if (c == '\n' || c == '\r' || c == (char)27) { FinishEditing(); return; }
        if (c == '\b')
        {
            if (searchText.Length > 0) searchText = searchText.Substring(0, searchText.Length - 1);
        }
        else if (!char.IsControl(c) && searchText.Length < 100) searchText += c;
        ApplyFilters();
    }

    void StartEditingSearch()
    {
        ClosePopup();
        editing = 0;
        filterKeyboard.SetActive(true);
        RefreshFilterLabels();
    }

    void FinishEditing()
    {
        editing = -1;
        if (filterKeyboard != null) filterKeyboard.SetActive(false);
        RefreshFilterLabels();
    }

    // ---------- Applicazione dei filtri ----------

    static bool Same(string a, string b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

    static bool ContainsText(string value, string query) =>
        (value ?? "").IndexOf(query.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;

    static string OwnerOf(PrinceProcess p) => string.IsNullOrWhiteSpace(p.user) ? "Senza owner" : p.user.Trim();
    static string StatusOf(PrinceProcess p) => string.IsNullOrWhiteSpace(p.status) ? "Senza stato" : p.status.Trim();
    static bool IsCamundaRow(PrinceProcess p) => p.id != null && p.id.StartsWith("camunda:", StringComparison.Ordinal);

    static bool TryUploadedDate(PrinceProcess p, out DateTime date)
    {
        if (DateTimeOffset.TryParse(p.uploaded, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value))
        {
            date = value.ToLocalTime().DateTime;
            return true;
        }
        date = DateTime.MinValue;
        return false;
    }

    /// <summary>
    /// Base come nella pagina web (opzioni spente): processi Prince non deprecati + tutte le
    /// definizioni Camunda (ogni versione). "Tutte le versioni" aggiunge i record Prince DEPRECATED
    /// (le versioni superate); "Nascondi deprecati" toglie anche le definizioni Camunda collegate a
    /// un processo deprecato. Queste due regole sono provvisorie, da confermare sui conteggi del web.
    /// </summary>
    void ApplyFilters(bool redraw = true)
    {
        filtered.Clear();
        var statusById = new Dictionary<string, string>();
        foreach (var p in processes)
            if (!IsCamundaRow(p) && !string.IsNullOrEmpty(p.id)) statusById[p.id] = p.status;

        foreach (var p in processes)
        {
            bool camundaRow = IsCamundaRow(p);
            if (!camundaRow && Same(p.status, "DEPRECATED") && (!allVersions || hideDeprecated)) continue;
            if (camundaRow && hideDeprecated)
            {
                string link = p.Latest?.processLinked;
                if (link != null && statusById.TryGetValue(link, out var linkedStatus) && Same(linkedStatus, "DEPRECATED")) continue;
            }
            if (!allOwners && !Same(p.user, username)) continue;
            if (ownerFilter.Length > 0 && !Same(OwnerOf(p), ownerFilter)) continue;
            if (statusFilter.Length > 0 && !Same(StatusOf(p), statusFilter)) continue;
            if (searchText.Trim().Length > 0)
            {
                string searchable = string.Join(" ", p.DisplayName, p.processName, p.id, p.processId, p.filename, OwnerOf(p));
                if (!ContainsText(searchable, searchText)) continue;
            }
            if (dateFrom.HasValue || dateTo.HasValue)
            {
                if (!TryUploadedDate(p, out var date)) continue;
                if (dateFrom.HasValue && date.Date < dateFrom.Value.Date) continue;
                if (dateTo.HasValue && date.Date > dateTo.Value.Date) continue;
            }
            filtered.Add(p);
        }
        filtered.Sort(CompareProcesses);
        page = 0;
        RefreshFilterLabels();
        if (redraw) ShowPage();
    }

    int CompareProcesses(PrinceProcess a, PrinceProcess b)
    {
        int result;
        switch (sortField)
        {
            case 0: result = string.Compare(a.processId ?? a.id, b.processId ?? b.id, StringComparison.OrdinalIgnoreCase); break;
            case 1: result = string.Compare(a.DisplayName, b.DisplayName, StringComparison.CurrentCultureIgnoreCase); break;
            case 2: result = string.Compare(OwnerOf(a), OwnerOf(b), StringComparison.CurrentCultureIgnoreCase); break;
            case 3: result = a.version.CompareTo(b.version); break;
            case 5: result = string.Compare(StatusOf(a), StatusOf(b), StringComparison.OrdinalIgnoreCase); break;
            default:
                TryUploadedDate(a, out var da);
                TryUploadedDate(b, out var db);
                result = da.CompareTo(db);
                break;
        }
        return sortDescending ? -result : result;
    }

    // ---------- Etichette ----------

    void RefreshFilterLabels()
    {
        if (searchLabel == null) return;
        searchLabel.text = "Cerca: " + (searchText.Length == 0 && editing != 0 ? "nome, ID, file, owner..." : searchText)
            + (editing == 0 ? " |" : "");
        statusLabel.text = "Stato: " + (statusFilter.Length == 0 ? "Tutti" : statusFilter);
        ownerLabel.text = "Owner: " + (ownerFilter.Length == 0 ? "Tutti" : ownerFilter);
        fromLabel.text = "Dal: " + FormatDate(dateFrom);
        toLabel.text = "Al: " + FormatDate(dateTo);
        deprecatedLabel.text = "Nascondi deprecati: " + YesNo(hideDeprecated);
        ownersLabel.text = "Di tutti gli owner: " + YesNo(allOwners);
        versionsLabel.text = "Tutte le versioni: " + YesNo(allVersions);
        sortLabel.text = "Ordina: " + SortNames[sortField];
        sortDirectionLabel.text = sortDescending ? "Decrescente" : "Crescente";
    }

    static string YesNo(bool value) => value ? "SI" : "NO";
    static string FormatDate(DateTime? date) => date.HasValue ? date.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) : "--";

    // ---------- Azioni ----------

    void SetQuickPeriod(int days, bool month = false)
    {
        DateTime today = DateTime.Today;
        dateTo = today;
        dateFrom = month ? today.AddMonths(-1) : today.AddDays(-days);
        ClosePopup();
        ApplyFilters();
    }

    void ResetFilters()
    {
        searchText = statusFilter = ownerFilter = "";
        dateFrom = dateTo = null;
        hideDeprecated = false;
        allOwners = true;
        allVersions = false;
        sortField = 4;
        sortDescending = true;
        FinishEditing();
        ClosePopup();
        ApplyFilters();
    }

    void OpenStatusPicker()
    {
        var options = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in processes) options.Add(StatusOf(p));
        OpenPicker("Stato", new List<string>(options), v => statusFilter = v);
    }

    void OpenOwnerPicker()
    {
        var options = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in processes) options.Add(OwnerOf(p));
        OpenPicker("Owner", new List<string>(options), v => ownerFilter = v);
    }

    // ---------- Popup sotto il pannello (selettori e date) ----------

    void ClosePopup()
    {
        if (filterPopup != null) filterPopup.SetActive(false);
    }

    void ClearPopup()
    {
        FinishEditing();
        for (int i = filterPopup.transform.childCount - 1; i >= 0; i--)
        {
            var child = filterPopup.transform.GetChild(i).gameObject;
            child.SetActive(false);
            Destroy(child);
        }
        filterPopup.SetActive(true);
    }

    float PopupY(int row) => filterBottom - .05f - row * FilterStep;

    void PopupBackground(int rows)
    {
        float height = rows * FilterStep + .03f;
        var bg = Quad(filterPopup.transform, "Sfondo", new Vector3(0f, filterBottom - .025f - height * .5f + .01f, 0f),
            new Vector2(filterInner + .03f, height), backgroundMat);
        bg.gameObject.name = "Sfondo popup";
    }

    void PopupTitle(string text, int row)
    {
        var label = Text(filterPopup.transform, text, new Vector3(0f, PopupY(row), -.009f),
            new Vector2(filterInner, FilterRowHeight), .024f);
        label.fontStyle = FontStyles.Bold;
    }

    void OpenPicker(string title, List<string> options, Action<string> choose)
    {
        options.Insert(0, "Tutti");
        pickerOptions = options;
        pickerChoose = choose;
        pickerTitle = title;
        pickerPage = 0;
        DrawPicker();
    }

    void DrawPicker()
    {
        ClearPopup();
        int perPage = PickerColumns * PickerRowsPerPage;
        int pages = Mathf.Max(1, Mathf.CeilToInt(pickerOptions.Count / (float)perPage));
        pickerPage = Mathf.Clamp(pickerPage, 0, pages - 1);

        PopupBackground(PickerRowsPerPage + 2);
        PopupTitle(pages > 1 ? $"{pickerTitle}  ({pickerPage + 1}/{pages})" : pickerTitle, 0);

        float cell = (filterInner - FilterGap * (PickerColumns - 1)) / PickerColumns;
        for (int i = 0; i < perPage; i++)
        {
            int index = pickerPage * perPage + i;
            if (index >= pickerOptions.Count) break;
            string value = pickerOptions[index];
            int col = i % PickerColumns, row = i / PickerColumns;
            float x = -filterInner * .5f + cell * .5f + col * (cell + FilterGap);
            PopupButton(new Vector3(x, PopupY(row + 1), -.009f), new Vector2(cell, FilterRowHeight), value, () =>
            {
                pickerChoose(value == "Tutti" ? "" : value);
                ClosePopup();
                ApplyFilters();
            });
        }

        float third = (filterInner - FilterGap * 2f) / 3f;
        int navRow = PickerRowsPerPage + 1;
        PopupButton(new Vector3(-third - FilterGap, PopupY(navRow), -.009f), new Vector2(third, FilterRowHeight), "< Prec.",
            () => { pickerPage--; DrawPicker(); });
        PopupButton(new Vector3(0f, PopupY(navRow), -.009f), new Vector2(third, FilterRowHeight), "Chiudi", ClosePopup);
        PopupButton(new Vector3(third + FilterGap, PopupY(navRow), -.009f), new Vector2(third, FilterRowHeight), "Succ. >",
            () => { pickerPage++; DrawPicker(); });
    }

    void OpenDatePicker(bool from)
    {
        ClearPopup();
        DateTime? current = from ? dateFrom : dateTo;
        PopupBackground(4);
        PopupTitle((from ? "Dal: " : "Al: ") + FormatDate(current), 0);

        void Shift(Func<DateTime, DateTime> change)
        {
            DateTime value = change((from ? dateFrom : dateTo) ?? DateTime.Today);
            if (from) dateFrom = value; else dateTo = value;
            ApplyFilters();
            OpenDatePicker(from);
        }

        float third = (filterInner - FilterGap * 2f) / 3f;
        var size = new Vector2(third, FilterRowHeight);
        float[] xs = { -third - FilterGap, 0f, third + FilterGap };
        PopupButton(new Vector3(xs[0], PopupY(1), -.009f), size, "-1 mese", () => Shift(d => d.AddMonths(-1)));
        PopupButton(new Vector3(xs[1], PopupY(1), -.009f), size, "-7 giorni", () => Shift(d => d.AddDays(-7)));
        PopupButton(new Vector3(xs[2], PopupY(1), -.009f), size, "-1 giorno", () => Shift(d => d.AddDays(-1)));
        PopupButton(new Vector3(xs[0], PopupY(2), -.009f), size, "+1 giorno", () => Shift(d => d.AddDays(1)));
        PopupButton(new Vector3(xs[1], PopupY(2), -.009f), size, "+7 giorni", () => Shift(d => d.AddDays(7)));
        PopupButton(new Vector3(xs[2], PopupY(2), -.009f), size, "+1 mese", () => Shift(d => d.AddMonths(1)));
        PopupButton(new Vector3(xs[0], PopupY(3), -.009f), size, "Oggi", () => Shift(_ => DateTime.Today));
        PopupButton(new Vector3(xs[1], PopupY(3), -.009f), size, "Nessuna data", () =>
        {
            if (from) dateFrom = null; else dateTo = null;
            ApplyFilters();
            OpenDatePicker(from);
        });
        PopupButton(new Vector3(xs[2], PopupY(3), -.009f), size, "Fine", ClosePopup);
    }

    // ---------- Costruzione ----------

    TMP_Text FilterButton(Vector3 position, Vector2 size, string text, UnityAction action, Transform parent = null)
    {
        Button(position, size, text, action);
        Transform button = transform.GetChild(transform.childCount - 1);
        if (parent != null) button.SetParent(parent, false);
        return button.GetComponentInChildren<TMP_Text>();
    }

    TMP_Text PopupButton(Vector3 position, Vector2 size, string text, UnityAction action) =>
        FilterButton(position, size, text, action, filterPopup.transform);

    void BuildFilters(float top, float inner, float bottom)
    {
        filterInner = inner;
        filterBottom = bottom;
        float half = (inner - FilterGap) * .5f;
        float third = (inner - FilterGap * 2f) / 3f;
        float leftHalf = -half * .5f - FilterGap * .5f, rightHalf = -leftHalf;
        float Y(int row) => top - .027f - row * FilterStep;
        var halfSize = new Vector2(half, FilterRowHeight);
        var thirdSize = new Vector2(third, FilterRowHeight);

        // RICERCA
        searchLabel = FilterButton(new Vector3(0f, Y(0), -.009f), new Vector2(inner, FilterRowHeight), "", StartEditingSearch);
        searchLabel.alignment = TextAlignmentOptions.Left;
        statusLabel = FilterButton(new Vector3(leftHalf, Y(1), -.009f), halfSize, "", OpenStatusPicker);
        ownerLabel = FilterButton(new Vector3(rightHalf, Y(1), -.009f), halfSize, "", OpenOwnerPicker);

        // PERIODO
        fromLabel = FilterButton(new Vector3(leftHalf, Y(2), -.009f), halfSize, "", () => OpenDatePicker(true));
        toLabel = FilterButton(new Vector3(rightHalf, Y(2), -.009f), halfSize, "", () => OpenDatePicker(false));
        FilterButton(new Vector3(-third - FilterGap, Y(3), -.009f), thirdSize, "Mese", () => SetQuickPeriod(0, true));
        FilterButton(new Vector3(0f, Y(3), -.009f), thirdSize, "Settimana", () => SetQuickPeriod(7));
        FilterButton(new Vector3(third + FilterGap, Y(3), -.009f), thirdSize, "Giorno", () => SetQuickPeriod(0));

        // OPZIONI
        deprecatedLabel = FilterButton(new Vector3(leftHalf, Y(4), -.009f), halfSize, "",
            () => { hideDeprecated = !hideDeprecated; ApplyFilters(); });
        ownersLabel = FilterButton(new Vector3(rightHalf, Y(4), -.009f), halfSize, "",
            () => { allOwners = !allOwners; ApplyFilters(); });
        versionsLabel = FilterButton(new Vector3(leftHalf, Y(5), -.009f), halfSize, "",
            () => { allVersions = !allVersions; ApplyFilters(); });
        FilterButton(new Vector3(rightHalf, Y(5), -.009f), halfSize, "Azzera filtri", ResetFilters);

        // ORDINAMENTO (le colonne della tabella web)
        sortLabel = FilterButton(new Vector3(leftHalf, Y(6), -.009f), halfSize, "",
            () => { sortField = (sortField + 1) % SortNames.Length; ApplyFilters(); });
        sortDirectionLabel = FilterButton(new Vector3(rightHalf, Y(6), -.009f), halfSize, "",
            () => { sortDescending = !sortDescending; ApplyFilters(); });

        // Tastiera virtuale per la ricerca, sotto il pannello
        filterKeyboard = new GameObject("Tastiera filtri");
        filterKeyboard.transform.SetParent(transform, false);
        string[] keys = { "1234567890", "qwertyuiop", "asdfghjkl", "zxcvbnm_-" };
        float step = inner / 10f;
        for (int row = 0; row < keys.Length; row++)
            for (int col = 0; col < keys[row].Length; col++)
            {
                char character = keys[row][col];
                FilterButton(new Vector3((col - 4.5f) * step, bottom - .05f - row * .052f, -.009f),
                    new Vector2(step - .005f, .046f), character.ToString(), () => TypeCharacter(character), filterKeyboard.transform);
            }
        FilterButton(new Vector3(-inner * .33f, bottom - .266f, -.009f), new Vector2(inner * .3f, .048f), "Spazio",
            () => TypeCharacter(' '), filterKeyboard.transform);
        FilterButton(new Vector3(0f, bottom - .266f, -.009f), new Vector2(inner * .3f, .048f), "Cancella",
            () => TypeCharacter('\b'), filterKeyboard.transform);
        FilterButton(new Vector3(inner * .33f, bottom - .266f, -.009f), new Vector2(inner * .3f, .048f), "Fine",
            FinishEditing, filterKeyboard.transform);
        filterKeyboard.SetActive(false);

        filterPopup = new GameObject("Popup filtri");
        filterPopup.transform.SetParent(transform, false);
        filterPopup.SetActive(false);

        RefreshFilterLabels();
    }
}
