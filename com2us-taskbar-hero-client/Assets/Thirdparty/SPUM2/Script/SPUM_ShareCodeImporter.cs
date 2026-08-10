using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

public class SPUM_ShareCodeImporter : MonoBehaviour
{
    #region Fields

    [Header("UI References")]
    public InputField codeInputField;
    public Text statusText;
    public Button loadButton;
    public Button applyButton;
    public Button closeButton;
    public Button showPopupButton;
    public Button copyButton;
    public GameObject popupPanel;
    public GameObject resultScrollView;
    public Text resultText;

    [Header("SPUM References")]
    public SPUM_Manager spumManager;

    private const string DefaultStatus = "Paste a web code and press Load.";
    private List<PreviewMatchingElement> _loadedElements;
    private string _loadedUnitType;

    private enum ButtonPhase { None, Copy, Load, Apply }

    #endregion

    #region Unity methods

    private void Awake()
    {
        if (spumManager == null) spumManager = FindAnyObjectByType<SPUM_Manager>();
    }

    private void Start()
    {
        InitializeUI();
        SetupEventListeners();
        HidePopup();
    }

    #endregion

    #region UI

    private void InitializeUI()
    {
        if (statusText != null)
        {
            statusText.text = DefaultStatus;
        }

        if (codeInputField != null)
        {
            codeInputField.lineType = InputField.LineType.MultiLineNewline;
            codeInputField.characterLimit = 20000;
        }
    }

    private void SetupEventListeners()
    {
        if (loadButton != null) loadButton.onClick.AddListener(LoadCode);
        if (applyButton != null) applyButton.onClick.AddListener(ApplyWebCode);
        if (closeButton != null) closeButton.onClick.AddListener(HidePopup);
        if (showPopupButton != null) showPopupButton.onClick.AddListener(ShowPopup);
        if (copyButton != null) copyButton.onClick.AddListener(CopyCodeToClipboard);
        if (codeInputField != null) codeInputField.onValueChanged.AddListener(OnCodeChanged);
    }

    private void OnCodeChanged(string code)
    {
        SetButtonState(string.IsNullOrWhiteSpace(code) ? ButtonPhase.None : ButtonPhase.Load);
    }

    public void ShowPopup()
    {
        if (popupPanel != null)
        {
            popupPanel.SetActive(true);
        }

        ResetToLoadState();

        // 현재 캐릭터의 Share Code를 자동 생성하여 inputField에 세팅
        var code = ExportShareCode();
        if (codeInputField != null && !string.IsNullOrEmpty(code))
        {
            codeInputField.text = code;
            codeInputField.ActivateInputField();
            SetButtonState(ButtonPhase.Copy);
        }
    }

    public void HidePopup()
    {
        if (popupPanel != null)
        {
            popupPanel.SetActive(false);
        }
    }

    #endregion

    #region Load / State

    private void ResetToLoadState()
    {
        _loadedElements = null;
        _loadedUnitType = null;
        ShowInputView();
        SetButtonState(ButtonPhase.None);
        UpdateStatus(DefaultStatus);
    }

    private void ShowInputView()
    {
        if (codeInputField != null) codeInputField.gameObject.SetActive(true);
        if (resultScrollView != null) resultScrollView.SetActive(false);
    }

    private void ShowResultView(string summary)
    {
        if (codeInputField != null) codeInputField.gameObject.SetActive(false);
        if (resultScrollView != null) resultScrollView.SetActive(true);
        if (resultText != null) resultText.text = summary;
    }

    private void SetButtonState(ButtonPhase phase)
    {
        if (copyButton != null) copyButton.gameObject.SetActive(phase == ButtonPhase.Copy);
        if (loadButton != null) loadButton.gameObject.SetActive(phase == ButtonPhase.Load);
        if (applyButton != null) applyButton.gameObject.SetActive(phase == ButtonPhase.Apply);
    }

    private void LoadCode()
    {
        string rawCode = codeInputField != null ? SanitizeInput(codeInputField.text) : string.Empty;
        if (string.IsNullOrWhiteSpace(rawCode))
        {
            UpdateStatus("Code is empty.");
            return;
        }

        if (spumManager == null)
        {
            UpdateStatus("SPUM_Manager reference is missing.");
            return;
        }

        if (!TryDecodePayload(rawCode, out var payloadJson, out var decodeError))
        {
            UpdateStatus($"Decode failed: {decodeError}");
            return;
        }

        if (!TryResolveEquipment(payloadJson, out var resolvedElements, out var targetUnitType, out var resolveSummary))
        {
            UpdateStatus(resolveSummary);
            return;
        }

        _loadedElements = resolvedElements;
        _loadedUnitType = targetUnitType;
        SetButtonState(ButtonPhase.Apply);
        ShowResultView(resolveSummary);
        UpdateStatus("Loaded. Press Apply to confirm.");
    }

    #endregion

    #region Apply

    public void ApplyWebCode()
    {
        if (_loadedElements == null || _loadedElements.Count == 0)
        {
            Fail("No loaded data. Press Load first.");
            return;
        }

        if (!string.IsNullOrEmpty(_loadedUnitType))
        {
            spumManager.SetType(_loadedUnitType);
        }

        spumManager.ItemResetAll();
        spumManager.SetSprite(_loadedElements);
        spumManager.ItemLoadButtonActive(_loadedElements);
        spumManager.animationManager.PlayFirstAnimation();

        if (spumManager.UIManager != null)
        {
            spumManager.UIManager.ToastOn("Applied web character code");
        }

        if (codeInputField != null) codeInputField.text = string.Empty;
        _loadedElements = null;
        _loadedUnitType = null;
        HidePopup();
    }

    #endregion

    #region Payload Decode

    private bool TryDecodePayload(string rawCode, out Dictionary<string, object> payloadJson, out string error)
    {
        payloadJson = null;
        error = null;

        try
        {
            string decoded = DecodeBase64(rawCode);
            payloadJson = SPUMJSON.DeserializeObject(decoded);
            if (payloadJson != null) return true;
        }
        catch (Exception firstException)
        {
            try
            {
                payloadJson = SPUMJSON.DeserializeObject(rawCode);
                if (payloadJson != null) return true;
            }
            catch (Exception secondException)
            {
                error = $"Invalid web code. Decode error: {firstException.Message} / JSON error: {secondException.Message}";
                return false;
            }
        }

        error = "Invalid JSON payload.";
        return false;
    }

    private string DecodeBase64(string rawCode)
    {
        string sanitized = SanitizeInput(rawCode);
        string normalized = sanitized.Replace('-', '+').Replace('_', '/');
        int padding = 4 - normalized.Length % 4;
        if (padding < 4)
        {
            normalized = normalized.PadRight(normalized.Length + padding, '=');
        }

        byte[] bytes = Convert.FromBase64String(normalized);
        return Encoding.UTF8.GetString(bytes);
    }

    private static string SanitizeInput(string input)
    {
        var sb = new StringBuilder(input.Length);
        foreach (char c in input)
        {
            if (c == '\uFEFF' || c == '\u200B' || c == '\u200C' || c == '\u200D' ||
                c == '\uFFFE' || c == '\r' || c == '\n' || c == '\u00A0')
                continue;

            sb.Append(c == '\t' ? ' ' : c);
        }
        return sb.ToString().Trim();
    }

    #endregion

    #region Resolve

    private bool TryResolveEquipment(Dictionary<string, object> payloadJson, out List<PreviewMatchingElement> resolvedElements, out string targetUnitType, out string summary)
    {
        resolvedElements = new List<PreviewMatchingElement>();
        targetUnitType = SPUMJSON.GetString(payloadJson, "unitType", "Unit");
        var resolved = new List<string>();
        var warnings = new List<string>();

        var equipment = SPUMJSON.GetObject(payloadJson, "equipment");
        var colors = SPUMJSON.GetObject(payloadJson, "colors");
        var directions = SPUMJSON.GetObject(payloadJson, "directions");
        var masks = SPUMJSON.GetObject(payloadJson, "masks");

        int? hairMaskOverride = null;
        object hairMaskToken = null;
        masks?.TryGetValue("hair", out hairMaskToken);
        if (hairMaskToken != null && TryParseHairMaskOverride(hairMaskToken, out int parsedHairMask))
        {
            hairMaskOverride = parsedHairMask;
        }

        if (equipment == null || equipment.Count == 0)
        {
            summary = "Payload has no equipment entries.";
            return false;
        }

        foreach (var entry in equipment)
        {
            string slotKey = entry.Key;
            object entryValue = entry.Value;

            var entryObj = entryValue as Dictionary<string, object>;
            if (entryObj == null) continue;
            string pkg = SPUMJSON.GetString(entryObj, "package", string.Empty);
            string textureName = SPUMJSON.GetString(entryObj, "name", string.Empty);

            if (string.IsNullOrWhiteSpace(pkg) || string.IsNullOrWhiteSpace(textureName))
            {
                continue;
            }

            string partType = slotKey.Trim();
            string dirFromKey = string.Empty;
            if (partType.EndsWith("_Right", StringComparison.Ordinal)) { partType = partType.Substring(0, partType.Length - 6); dirFromKey = "Right"; }
            else if (partType.EndsWith("_Left", StringComparison.Ordinal)) { partType = partType.Substring(0, partType.Length - 5); dirFromKey = "Left"; }
            else if (partType.EndsWith("_Front", StringComparison.Ordinal)) { partType = partType.Substring(0, partType.Length - 6); dirFromKey = "Front"; }

            string unitType = targetUnitType;

            if (!TryGetColor(colors, slotKey, out Color resolvedColor))
            {
                resolvedColor = Color.white;
            }

            List<SpumTextureData> textures = spumManager.ExtractTextureData(pkg, unitType, partType, textureName);

            // Fallback: strip partType prefix (e.g., "body_zombie_2" → "zombie_2")
            if ((textures == null || textures.Count == 0) &&
                textureName.StartsWith(partType + "_", StringComparison.OrdinalIgnoreCase))
            {
                textureName = textureName.Substring(partType.Length + 1);
                textures = spumManager.ExtractTextureData(pkg, unitType, partType, textureName);
            }

            // Fallback: use partType as texture name (e.g., eye → "Eye")
            if (textures == null || textures.Count == 0)
            {
                textures = spumManager.ExtractTextureData(pkg, unitType, partType, partType);
                if (textures != null && textures.Count > 0) textureName = partType;
            }

            // Fallback: individual sprites by numeric suffix (e.g., "cloth010" → Cloth_Body_010, Cloth_Left_010, ...)
            bool isIndividual = false;
            if (textures == null || textures.Count == 0)
            {
                string suffix = ExtractNumericSuffix(textureName);
                if (!string.IsNullOrEmpty(suffix))
                {
                    textures = spumManager.spumPackages
                        .Where(p => p.Name == pkg)
                        .SelectMany(p => p.SpumTextureData)
                        .Where(t =>
                            string.Equals(t.UnitType, unitType, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(t.PartType, partType, StringComparison.OrdinalIgnoreCase) &&
                            t.Name.EndsWith("_" + suffix, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    if (textures.Count > 0)
                    {
                        isIndividual = true;
                    }
                }
            }

            if (textures == null || textures.Count == 0)
            {
                warnings.Add($"No texture group: {pkg}/{unitType}/{partType}/{textureName}");
                continue;
            }

            int? slotMaskIndex = string.Equals(slotKey, "hair", StringComparison.OrdinalIgnoreCase)
                ? hairMaskOverride
                : null;

            string dir = !string.IsNullOrEmpty(dirFromKey) ? dirFromKey : SPUMJSON.GetString(directions, slotKey, string.Empty);
            var elements = BuildElements(dir, unitType, partType, textures, resolvedColor, slotMaskIndex, isIndividual);
            resolvedElements.AddRange(elements);
            resolved.Add($"{slotKey}: {pkg}/{textureName}");
        }

        if (resolvedElements.Count == 0)
        {
            summary = warnings.Count > 0 ? string.Join("\n", warnings) : "No items were resolved.";
            return false;
        }

        summary = string.Join("\n", resolved);
        if (warnings.Count > 0)
        {
            summary += "\n---\n" + string.Join("\n", warnings);
        }

        return true;
    }

    private List<PreviewMatchingElement> BuildElements(string dir, string unitType, string partType, List<SpumTextureData> textures, Color color, int? maskIndex = null, bool isIndividual = false)
    {
        int mask = maskIndex ?? 0;
        return textures.Select(texture =>
        {
            string structure = ResolveStructure(partType, texture, isIndividual);

            return new PreviewMatchingElement
            {
                UnitType = unitType, PartType = texture.PartType ?? partType, PartSubType = texture.PartSubType,
                Dir = dir, ItemPath = texture.Path,
                Structure = structure,
                MaskIndex = mask, Color = color
            };
        }).ToList();
    }

    private string ResolveStructure(string partType, SpumTextureData texture, bool isIndividual)
    {
        string originalPartType = texture?.PartType ?? partType;
        if (texture == null) return originalPartType;
        if (isIndividual)
        {
            string[] tokens = texture.SubType.Split('_');
            if (tokens.Length >= 3 && string.Equals(tokens[0], partType, StringComparison.OrdinalIgnoreCase))
                return tokens[1];
        }
        return texture.SubType.Equals(texture.Name, StringComparison.OrdinalIgnoreCase) ? originalPartType : texture.SubType;
    }

    #endregion

    #region Utilities

    private void CopyCodeToClipboard()
    {
        if (codeInputField == null || string.IsNullOrEmpty(codeInputField.text)) return;
        GUIUtility.systemCopyBuffer = codeInputField.text;
        UpdateStatus("Copied to clipboard.");
    }

    /// <summary>
    /// ItemPath에서 패키지의 실제 텍스처 Name을 역검색합니다.
    /// 개별 스프라이트(Cloth_Left_01 등)는 Path가 텍스처 Name과 다르므로 패키지에서 찾아야 합니다.
    /// </summary>
    private string ResolveTextureNameFromPath(string packageName, string itemPath, string partType)
    {
        // 패키지에서 동일 Path를 가진 텍스처의 Name을 찾는다
        if (spumManager != null)
        {
            var match = spumManager.spumPackages
                .Where(p => p.Name == packageName)
                .SelectMany(p => p.SpumTextureData)
                .FirstOrDefault(t => string.Equals(t.Path, itemPath, StringComparison.OrdinalIgnoreCase));
            if (match != null)
                return match.Name;
        }

        // 폴백: 경로의 마지막 세그먼트 사용
        string[] parts = itemPath.Replace("\\", "/").Split('/');
        return parts[parts.Length - 1];
    }

    private string ExtractNumericSuffix(string text)
    {
        int i = text.Length - 1;
        while (i >= 0 && char.IsDigit(text[i])) i--;
        return i < text.Length - 1 ? text.Substring(i + 1) : null;
    }

    private bool TryGetColor(Dictionary<string, object> colors, string slotKey, out Color color)
    {
        color = Color.white;
        string colorText = SPUMJSON.GetString(colors, slotKey);
        if (string.IsNullOrWhiteSpace(colorText)) return false;
        if (!colorText.StartsWith("#", StringComparison.Ordinal)) colorText = "#" + colorText;
        return ColorUtility.TryParseHtmlString(colorText, out color);
    }

    private bool TryParseHairMaskOverride(object token, out int maskIndex)
    {
        maskIndex = (int)SpriteMaskInteraction.None;
        if (token == null || token is bool) return false;
        if (!Enum.TryParse<SpriteMaskInteraction>(token.ToString().Trim(), true, out var parsed)) return false;
        maskIndex = (int)parsed;
        return true;
    }

    #endregion

    #region Status / Log

    private void UpdateStatus(string message)
    {
        if (statusText != null) statusText.text = message;
        Debug.Log($"SPUM ShareCodeImporter: {message}");
    }

    private void Fail(string message)
    {
        UpdateStatus(message);
        if (spumManager != null && spumManager.UIManager != null)
            spumManager.UIManager.ToastOn(message);
    }

    #endregion

    #region Export

    /// <summary>
    /// 현재 캐릭터 상태에서 Share Code(Base64)를 생성합니다.
    /// </summary>
    public string ExportShareCode()
    {
        if (spumManager == null)
        {
            Debug.LogWarning("SPUM ShareCodeExporter: SPUM_Manager reference is missing.");
            return null;
        }

        var matchingList = spumManager.PreviewPrefab.GetComponentInChildren<SPUM_MatchingList>(true);
        if (matchingList == null || matchingList.matchingTables == null || matchingList.matchingTables.Count == 0)
        {
            Debug.LogWarning("SPUM ShareCodeExporter: No MatchingList found in PreviewPrefab.");
            return null;
        }

        var equipment = new Dictionary<string, object>();
        var colors = new Dictionary<string, object>();
        var directions = new Dictionary<string, object>();
        var masks = new Dictionary<string, object>();
        var processedSlots = new HashSet<string>();

        // ignoreColorPart 조회용 — 파츠별 색상 무시 SubType 목록
        var ignoreColorMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
#if UNITY_2023_1_OR_NEWER
        var spriteButtons = FindObjectsByType<SPUM_SpriteButtonST>(FindObjectsSortMode.None);
#else
        #pragma warning disable CS0618
        var spriteButtons = FindObjectsOfType<SPUM_SpriteButtonST>();
        #pragma warning restore CS0618
#endif
        foreach (var btn in spriteButtons)
        {
            string btnName = btn.gameObject.name;
            if (btn.ignoreColorPart != null && btn.ignoreColorPart.Count > 0 && !ignoreColorMap.ContainsKey(btnName))
            {
                ignoreColorMap[btnName] = btn.ignoreColorPart;
            }
        }

        foreach (var elem in matchingList.matchingTables)
        {
            if (elem.renderer == null || elem.renderer.sprite == null) continue;
            if (string.IsNullOrEmpty(elem.ItemPath)) continue;

            // ItemPath: "Addons/Legacy/0_Unit/0_Sprite/0_Eye/Eye0" or "Legacy/0_Unit/..."
            string[] pathParts = elem.ItemPath.Replace("\\", "/").Split('/');
            if (pathParts.Length < 2) continue;

            // Addons/ prefix 제거 — spumPackages.Name은 "Legacy", "Ver121" 등
            string packageName = pathParts[0];
            if (packageName == "Addons" && pathParts.Length >= 3)
            {
                packageName = pathParts[1];
            }

            string partType = elem.PartType;
            if (string.IsNullOrEmpty(partType)) continue;

            // ItemPath에서 실제 텍스처 Name을 역검색
            string textureName = ResolveTextureNameFromPath(packageName, elem.ItemPath, partType);

            // slotKey 생성 (Dir 포함)
            string slotKey = partType.ToLower();
            if (!string.IsNullOrEmpty(elem.Dir))
            {
                slotKey = partType.ToLower() + "_" + elem.Dir;
            }

            // 색상: ignoreColorPart에 해당하지 않는 요소에서 가져옴 (이미 처리된 슬롯도 색상은 갱신 가능)
            string structure = elem.renderer.sprite.name;
            bool isIgnored = false;
            if (ignoreColorMap.TryGetValue(partType, out var ignoreList))
            {
                isIgnored = ignoreList.Contains(structure);
            }

            if (!isIgnored && !colors.ContainsKey(slotKey))
            {
                Color32 c32 = elem.Color;
                if (c32.a > 0)
                {
                    colors[slotKey] = "#" + ColorUtility.ToHtmlStringRGB(elem.Color).ToLower();
                }
            }

            // 이미 처리된 슬롯은 건너뜀 (같은 부위의 여러 스프라이트 중 첫 번째만)
            if (processedSlots.Contains(slotKey)) continue;
            processedSlots.Add(slotKey);

            // equipment
            equipment[slotKey] = new Dictionary<string, object>
            {
                ["package"] = packageName,
                ["name"] = textureName
            };

            // directions
            if (!string.IsNullOrEmpty(elem.Dir))
            {
                directions[slotKey] = elem.Dir;
            }

            // masks
            if (elem.MaskIndex != 0)
            {
                masks[slotKey] = ((SpriteMaskInteraction)elem.MaskIndex).ToString();
            }
        }

        if (equipment.Count == 0)
        {
            Debug.LogWarning("SPUM ShareCodeExporter: No equipment data found.");
            return null;
        }

        var payload = new Dictionary<string, object>
        {
            ["v"] = spumManager._version,
            ["source"] = $"unity_{Application.unityVersion}",
            ["createdAt"] = DateTime.UtcNow.ToString("o"),
            ["unitType"] = "Unit",
            ["equipment"] = equipment,
            ["colors"] = colors,
            ["directions"] = directions,
            ["masks"] = masks
        };

        string json = SPUMJSON.Serialize(payload);
        string base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        return base64;
    }

    #endregion
}
