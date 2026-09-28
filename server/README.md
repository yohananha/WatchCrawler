# Garmin Achievements – שכבת ה-LLM

שרת קטן ב-ASP.NET Core (‎.NET 10, בלי חבילות חיצוניות) שמקבל אירוע מהשעון ומחזיר הישג סרקסטי.
כולל מצב השוואה עיוורת בין ספקי LLM.

## מבנה

| קובץ | תפקיד |
|---|---|
| `Models.cs` | אירוע, הישג, הגדרות |
| `Providers.cs` | ממשק `ILlmProvider` + מימוש Anthropic ומימוש OpenAI-compatible (DeepSeek, OpenRouter) |
| `Achievements.cs` | חישוב tier וצליל (דטרמיניסטי), בניית prompt, היסטוריה, generator עם retry ו-fallback |
| `Comparison.cs` | הרצת כל הספקים על אותם אירועים ודוח עיוור |
| `Program.cs` | מצב API או מצב השוואה, אימות מפתח משותף |
| `sample-events.json` | 10 אירועים לדוגמה – כדאי להחליף באירועים אמיתיים שלך |
| `Tests/` | בדיקות יחידה (xUnit) ל-`TierCalculator` ו-`AchievementGenerator` |
| `Dockerfile`, `fly.toml` | דיפלוי ל-Fly.io |

## הרצה

מגדירים מפתח רק לספקים שיש לך (ספק בלי מפתח פשוט מדולג):

```powershell
$env:ANTHROPIC_API_KEY = "sk-ant-..."
$env:DEEPSEEK_API_KEY  = "sk-..."
$env:OPENROUTER_API_KEY = "sk-or-..."   # וגם לעדכן Model ב-appsettings.json
```

### השוואה עיוורת

```bash
dotnet run -- compare sample-events.json           # הדוח מציג את הספק והמודל ליד כל תשובה
dotnet run -- compare sample-events.json --blind   # השוואה עיוורת
```

נוצרים שני קבצים:
- `compare-report.md` – לכל אירוע התשובות תחת A/B/C, עם עמודת Model. עם `--blind` העמודה מוסתרת והסדר מעורבב – נותנים ציון בלי להציץ.
- `compare-key.json` – איזו אות שייכת לאיזה ספק, ושגיאות לכל תשובה. במצב עיוור פותחים רק אחרי הציונים.

בקונסול מודפסת טבלה עם זמן תגובה ממוצע, טוקנים, fallbacks ועלות חודשית משוערת
(לפי `EstimatedEventsPerDay` והמחירים ב-appsettings).

### מצב API

```bash
dotnet run
```

```bash
curl -X POST http://localhost:5080/achievement \
  -H "Content-Type: application/json" \
  -H "X-Watch-Key: <ה-WATCH_SHARED_KEY שלך, אם מוגדר>" \
  -d '{"type":"sleep_summary","value":5.1,"unit":"hours","baselineMean":6.9,"baselineStd":0.6}'
```

אם `WATCH_SHARED_KEY` לא מוגדר (למשל בפיתוח מקומי) – האימות כבוי לגמרי.

### בדיקות

```bash
cd Tests && dotnet test
```

רץ אוטומטית ב-CI (`.github/workflows/ci.yml`) בכל push/PR.

### דיפלוי ל-Fly.io

```bash
fly auth login
fly apps create <שם-ייחודי>          # ולעדכן ב-fly.toml
fly secrets set ANTHROPIC_API_KEY=sk-ant-... WATCH_SHARED_KEY=<מחרוזת אקראית>
fly deploy
```

Fly מספק HTTPS אוטומטית (נדרש – Connect IQ מסרב לבקשות HTTP רגילות, קוד תגובה -1001
`SECURE_CONNECTION_REQUIRED`).

## החלטות עיצוב

- **ה-tier והצליל נקבעים בקוד, לא ע"י המודל.** z-score מול הבסיס האישי: ‎≤ ‎-1 → cursed,
  ‏‎≥ 0.6 → rare, ‏‎≥ 1.3 → epic, ‏‎≥ 2 → legendary. כך האנימציה בשעון עקבית.
- **המודל כותב רק title / text / reward.** JSON לא תקין או ארוך מדי → ניסיון נוסף; אם גם הוא ארוך – קיצוץ; אם נכשל לגמרי – בנק fallback קטן.
- **היסטוריית 10 ההישגים האחרונים** נשלחת במצב API כדי למנוע חזרות (כרגע בזיכרון, בהמשך DB).
- **Language** ב-appsettings קובע את שפת ההודעות. שים לב: לעברית על השעון יידרש פונט מותאם ב-Connect IQ.

## מה חסר (שלבים הבאים)

- שמירת היסטוריה ב-DB (כרגע בזיכרון בלבד – מתאפס בכל הפעלה מחדש).
- push לטלפון במקביל לתשובה לשעון.
- בדיקת אינטגרציה מלאה מקצה לקצה (שעון אמיתי → Fly.io → LLM אמיתי) – נבדק עד כה רק
  עד גבול ה-HTTPS (ראו `watch/README.md`).
