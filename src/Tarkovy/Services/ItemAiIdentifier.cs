using System.Drawing;
using System.Drawing.Imaging;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Tarkovy.Models;

namespace Tarkovy.Services;

/// <summary>
/// Last-resort identity: screenshot the stash, mark the click, send that photo to a
/// vision API, resolve the name on the local catalog, then delete the screenshot.
/// </summary>
internal static class ItemAiIdentifier
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(25) };
    private static readonly HttpClient CursorHttp = new() { Timeout = TimeSpan.FromSeconds(90) };

    public static bool IsConfigured =>
        App.Settings.ItemScanAiEnabled && !string.IsNullOrWhiteSpace(App.Settings.ItemScanAiApiKey);

    private static bool CursorSpendBlocked;

    public static async Task<(ItemDefinition? Item, string Note, string Raw)> IdentifyAsync(
        int clickX,
        int clickY,
        int slotW,
        int slotH,
        string? tooltipOcr,
        IReadOnlyList<ItemDefinition> candidates,
        ItemCatalog catalog,
        CancellationToken ct)
    {
        var key = App.Settings.ItemScanAiApiKey.Trim();
        var provider = (App.Settings.ItemScanAiProvider ?? "claude").Trim().ToLowerInvariant();
        if (key.Length == 0) return (null, "ai: no api key", "");
        if (provider == "cursor" && CursorSpendBlocked)
            return (null, "ai: Cursor skipped (hard limit this session — use Claude/ChatGPT/Gemini)", "");

        string? shotPath = null;
        try
        {
            var cap = ScreenCapture.CaptureIconScanRegion(clickX, clickY);
            int localX = cap.LocalX, localY = cap.LocalY, imgW, imgH;
            using (cap.Region)
            using (var marked = ScreenCapture.MarkClick(cap.Region, cap.LocalX, cap.LocalY))
            {
                imgW = marked.Width;
                imgH = marked.Height;
                shotPath = SaveTempPng(marked);
            }

            var png = Convert.ToBase64String(await File.ReadAllBytesAsync(shotPath, ct).ConfigureAwait(false));
            var prompt = BuildPrompt(clickX, clickY, localX, localY, imgW, imgH, slotW, slotH, tooltipOcr, candidates);
            var raw = provider switch
            {
                "openai" => await CallOpenAiAsync(key, png, prompt, ct).ConfigureAwait(false),
                "gemini" => await CallGeminiAsync(key, png, prompt, ct).ConfigureAwait(false),
                "cursor" => await CallCursorAsync(key, png, prompt, ct).ConfigureAwait(false),
                _ => await CallClaudeAsync(key, png, prompt, ct).ConfigureAwait(false)
            };
            var item = ResolveFromModelText(raw, catalog, candidates);
            var rawNote = Truncate(raw, 800);
            if (item == null)
                return (null, $"ai: no catalog hit (shot+click, deleted) | raw={rawNote}", raw);
            return (item, $"ai: {ItemDisplayNames.ShortName(item)} via {provider} shot+click | raw={rawNote}", raw);
        }
        catch (Exception ex)
        {
            if (provider == "cursor" && ex.Message.Contains("hard limit", StringComparison.OrdinalIgnoreCase))
                CursorSpendBlocked = true;
            return (null, $"ai: {ex.Message}", ex.ToString());
        }
        finally
        {
            DeleteTemp(shotPath);
        }
    }

    private static string SaveTempPng(Bitmap bmp)
    {
        var dir = Path.Combine(Path.GetTempPath(), "Tarkovy");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"ai-{Guid.NewGuid():N}.png");
        bmp.Save(path, ImageFormat.Png);
        return path;
    }

    private static void DeleteTemp(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try { File.Delete(path); }
        catch { /* screenshot is throwaway */ }
    }

    private static string BuildPrompt(
        int clickX, int clickY, int localX, int localY, int imgW, int imgH,
        int slotW, int slotH,
        string? tooltipOcr, IReadOnlyList<ItemDefinition> candidates)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You identify Escape from Tarkov inventory items from the attached screenshot.");
        sb.AppendLine($"Screenshot is {imgW}x{imgH} px. The lime crosshair and green box mark the click.");
        sb.AppendLine($"Click is at pixel ({localX},{localY}) in this image (screen {clickX},{clickY}).");
        sb.AppendLine("Identify ONLY the item in that marked cell (highlighted stash/inventory slot).");
        sb.AppendLine("Read the in-cell short name at the top of the cell and the tooltip if it is visible.");
        sb.AppendLine("Ammo codes (M882, FMJ, V-Max, Hawk, 7mm Buckshot…) are English even on Portuguese UI. Do not guess a different caliber.");
        sb.AppendLine($"Estimated grid size of the clicked item: {slotW}x{slotH} slots.");
        if (!string.IsNullOrWhiteSpace(tooltipOcr))
        {
            sb.AppendLine("Local OCR is often wrong — use it only as a weak hint:");
            sb.AppendLine(tooltipOcr.Trim());
        }
        sb.AppendLine("Candidates from the local Tarkovy catalog (prefer these ids when they match the photo):");
        var n = 0;
        foreach (var item in candidates)
        {
            if (++n > 40) break;
            var name = ItemDisplayNames.Name(item).Replace('\n', ' ');
            var sn = ItemDisplayNames.ShortName(item).Replace('\n', ' ');
            var en = ItemDisplayNames.CatalogName(item).Replace('\n', ' ');
            sb.AppendLine($"{item.Id}\t{sn}\t{name}\t{en}\t{item.Width}x{item.Height}");
        }
        sb.AppendLine("Reply with JSON only: {\"id\":\"<catalog id or empty>\",\"name\":\"<english or portuguese name>\"}");
        sb.AppendLine("Pick an id from the list when possible. Empty id if unknown. Do not invent ids.");
        sb.AppendLine("Do not write code, edit files, or use tools. JSON only.");
        return sb.ToString();
    }

    private static ItemDefinition? ResolveFromModelText(
        string raw, ItemCatalog catalog, IReadOnlyList<ItemDefinition> candidates)
    {
        var json = ExtractJson(raw);
        string? id = null;
        string? name = null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
                id = idEl.GetString();
            if (root.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
                name = nameEl.GetString();
        }
        catch
        {
            id = raw.Trim();
        }

        if (!string.IsNullOrWhiteSpace(id))
        {
            var byId = catalog.FindById(id.Trim());
            if (byId != null) return byId;
            var listed = candidates.FirstOrDefault(c =>
                c.Id.Equals(id.Trim(), StringComparison.OrdinalIgnoreCase));
            if (listed != null) return listed;
        }

        if (string.IsNullOrWhiteSpace(name)) return null;
        var fromTooltip = catalog.MatchByTooltip(name).Item;
        if (fromTooltip != null) return fromTooltip;
        var search = catalog.Search(name, 3);
        return search.Count > 0 ? search[0] : null;
    }

    private static string ExtractJson(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        return start >= 0 && end > start ? text[start..(end + 1)] : text;
    }

    private static async Task<string> CallCursorAsync(string key, string png, string prompt, CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(new
        {
            name = "Tarkovy item",
            prompt = new
            {
                text = prompt,
                images = new object[]
                {
                    new { data = png, mimeType = "image/png" }
                }
            }
        });

        using var create = new HttpRequestMessage(HttpMethod.Post, "https://api.cursor.com/v1/agents");
        create.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        create.Content = new StringContent(body, Encoding.UTF8, "application/json");
        using var created = await CursorHttp.SendAsync(create, ct).ConfigureAwait(false);
        var createdJson = await created.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!created.IsSuccessStatusCode)
            throw new HttpRequestException(ApiError("Cursor", created.StatusCode, createdJson));

        using var createdDoc = JsonDocument.Parse(createdJson);
        var root = createdDoc.RootElement;
        var agentId = root.GetProperty("agent").GetProperty("id").GetString()
                      ?? throw new InvalidOperationException("Cursor: agent id ausente");
        var runId = root.TryGetProperty("run", out var runEl) && runEl.TryGetProperty("id", out var runIdEl)
            ? runIdEl.GetString()
            : root.GetProperty("agent").GetProperty("latestRunId").GetString();
        if (string.IsNullOrWhiteSpace(runId))
            throw new InvalidOperationException("Cursor: run id ausente");

        try
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(75);
            while (DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Delay(1200, ct).ConfigureAwait(false);

                using var poll = new HttpRequestMessage(HttpMethod.Get,
                    $"https://api.cursor.com/v1/agents/{Uri.EscapeDataString(agentId)}/runs/{Uri.EscapeDataString(runId)}");
                poll.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
                using var polled = await CursorHttp.SendAsync(poll, ct).ConfigureAwait(false);
                var pollJson = await polled.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                if (!polled.IsSuccessStatusCode)
                    throw new HttpRequestException(ApiError("Cursor", polled.StatusCode, pollJson));

                using var pollDoc = JsonDocument.Parse(pollJson);
                var status = pollDoc.RootElement.TryGetProperty("status", out var st)
                    ? st.GetString() ?? ""
                    : "";
                if (status is "FINISHED" or "completed" or "COMPLETED")
                {
                    if (pollDoc.RootElement.TryGetProperty("result", out var result))
                    {
                        if (result.ValueKind == JsonValueKind.String)
                            return result.GetString() ?? "";
                        if (result.ValueKind == JsonValueKind.Object && result.TryGetProperty("text", out var text))
                            return text.GetString() ?? "";
                    }
                    return pollJson;
                }

                if (status is "ERROR" or "CANCELLED" or "EXPIRED" or "FAILED")
                    throw new InvalidOperationException($"Cursor run {status}: {Truncate(pollJson, 180)}");
            }

            throw new TimeoutException("Cursor: tempo esgotado aguardando o agente.");
        }
        finally
        {
            _ = ArchiveCursorAgentAsync(key, agentId);
        }
    }

    private static async Task ArchiveCursorAgentAsync(string key, string agentId)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Delete,
                $"https://api.cursor.com/v1/agents/{Uri.EscapeDataString(agentId)}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            using var resp = await CursorHttp.SendAsync(req).ConfigureAwait(false);
            if (resp.IsSuccessStatusCode) return;
            using var arch = new HttpRequestMessage(HttpMethod.Post,
                $"https://api.cursor.com/v1/agents/{Uri.EscapeDataString(agentId)}/archive");
            arch.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            using var archived = await CursorHttp.SendAsync(arch).ConfigureAwait(false);
        }
        catch
        {
            /* best-effort cleanup */
        }
    }

    private static async Task<string> CallClaudeAsync(string key, string png, string prompt, CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(new
        {
            model = "claude-sonnet-4-5",
            max_tokens = 200,
            messages = new object[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "image", source = new { type = "base64", media_type = "image/png", data = png } },
                        new { type = "text", text = prompt }
                    }
                }
            }
        });

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        req.Headers.TryAddWithoutValidation("x-api-key", key);
        req.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
        req.Content = new StringContent(body, Encoding.UTF8, "application/json");
        using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException(ApiError("Claude", resp.StatusCode, json));

        using var doc = JsonDocument.Parse(json);
        var sb = new StringBuilder();
        foreach (var block in doc.RootElement.GetProperty("content").EnumerateArray())
        {
            if (block.TryGetProperty("text", out var t))
                sb.Append(t.GetString());
        }
        return sb.ToString();
    }

    private static async Task<string> CallOpenAiAsync(string key, string png, string prompt, CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(new
        {
            model = "gpt-4o-mini",
            max_tokens = 200,
            messages = new object[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "image_url", image_url = new { url = "data:image/png;base64," + png } },
                        new { type = "text", text = prompt }
                    }
                }
            }
        });

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        req.Content = new StringContent(body, Encoding.UTF8, "application/json");
        using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException(ApiError("ChatGPT", resp.StatusCode, json));

        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()
               ?? "";
    }

    private static async Task<string> CallGeminiAsync(string key, string png, string prompt, CancellationToken ct)
    {
        var url =
            "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key="
            + Uri.EscapeDataString(key);
        var body = JsonSerializer.Serialize(new
        {
            contents = new object[]
            {
                new
                {
                    parts = new object[]
                    {
                        new { text = prompt },
                        new { inlineData = new { mimeType = "image/png", data = png } }
                    }
                }
            },
            generationConfig = new { maxOutputTokens = 200 }
        });

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Content = new StringContent(body, Encoding.UTF8, "application/json");
        using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException(ApiError("Gemini", resp.StatusCode, json));

        using var doc = JsonDocument.Parse(json);
        var sb = new StringBuilder();
        foreach (var cand in doc.RootElement.GetProperty("candidates").EnumerateArray())
        {
            if (!cand.TryGetProperty("content", out var content)) continue;
            if (!content.TryGetProperty("parts", out var parts)) continue;
            foreach (var part in parts.EnumerateArray())
            {
                if (part.TryGetProperty("text", out var t))
                    sb.Append(t.GetString());
            }
        }
        return sb.ToString();
    }

    private static string ApiError(string who, System.Net.HttpStatusCode code, string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out var err))
            {
                if (err.ValueKind == JsonValueKind.Object && err.TryGetProperty("message", out var msg))
                    return $"{who} {(int)code}: {msg.GetString()}";
                if (err.ValueKind == JsonValueKind.String)
                    return $"{who} {(int)code}: {err.GetString()}";
            }
        }
        catch { /* raw */ }
        return $"{who} {(int)code}: {Truncate(json, 180)}";
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) ? "" : s.Length <= max ? s : s[..max] + "…";
}
