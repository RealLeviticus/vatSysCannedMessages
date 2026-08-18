# vatSys Canned Messages

A vatSys plugin for sending canned private messages, with the message library
kept in this repository so controllers can share and improve templates.

Pick a message, choose your name from a list, pick a recipient, send. Anything a
template needs but does not know — your name, a mentor's name, a number of
minutes — becomes a dropdown or a text box in the window.

## Installing

1. Download the latest build (or build it yourself, see below).
2. Copy the `CannedMessages` folder into
   `Documents\vatSys Files\Profiles\<your profile>\Plugins\`.
3. Restart vatSys and open **Messages → Canned Messages**.

vatSys also loads plugins from `<install dir>\bin\Plugins`, which applies to
every profile but needs administrator rights to write to.

## Using it

| Control | What it does |
| --- | --- |
| **To** | Recipient callsign. Press **Online** to fill the list from who is connected; it pre-fills from the track selected on the ASD. |
| Message tree | Categories and messages from `templates/messages.json`. |
| Field rows | One row per placeholder the message needs. `{name}` becomes a dropdown of the shared name list. |
| Preview | Exactly what will be sent. Placeholders still shown in `{braces}` are not filled in yet, and **Send** stays disabled until they are. |
| **Send** | Sends it as a vatSys private message. It appears in your PM window like any other. |
| **Copy** | Puts the message on the clipboard instead — useful for the ATC chat or a coordination window. |
| **Refresh** | Pulls the latest `templates/` from this repository. |
| **Open folder** | Opens your local settings folder (see below). |

Messages longer than 200 characters are split across several private messages on
word boundaries. A `\n` in a template forces a new message.

## Writing templates

Templates live in [`templates/messages.json`](templates/messages.json) and the
shared name list in [`templates/names.json`](templates/names.json). Add yours and
open a pull request — every controller running the plugin picks it up on the next
refresh.

### `messages.json`

```json
{
  "version": 1,
  "categories": [
    {
      "name": "ATS Standards",
      "messages": [
        {
          "id": "std-multiple-atis",
          "title": "Multiple ATIS on DEL, GND or TWR",
          "text": "Hi, my name is {name}, hope you are doing well. ..."
        }
      ]
    }
  ]
}
```

| Key | Required | Notes |
| --- | --- | --- |
| `id` | yes | Unique and stable. A local template with the same `id` replaces the shared one. |
| `title` | yes | What shows in the message tree. |
| `text` | yes | The message. `{placeholders}` in braces. |
| `fields` | no | Extra control over the placeholder inputs. |

### Two rules for message text

vatSys sanitises everything before it hits the network, so write templates
around it:

- **No colons.** `Network.MakeNetworkSafe` replaces every `:` with a space,
  because the colon is the FSD field separator. Write URLs without the scheme —
  `vatpac.org/controllers/position`, not `https://vatpac.org/...` — or the link
  arrives broken.
- **ASCII only.** The text is round-tripped through the system ANSI codepage, so
  curly quotes, en dashes and emoji get mangled. Use `'` and `-`.

### Placeholders

Write them as `{key}`. Four fill themselves in and never ask you anything:

| Placeholder | Becomes |
| --- | --- |
| `{callsign}` | Your connected position, e.g. `ML-TBD_CTR` |
| `{recipient}` | The callsign in the **To** box |
| `{time}` | Current UTC time, `HHmm` |
| `{date}` | Current UTC date, `DDMMM` |

`{name}` is special: with no configuration it becomes a dropdown of
`templates/names.json`, which is the "Hi, my name is ..." case. Every other
placeholder becomes a free text box unless you describe it in `fields`.

### `fields`

```json
{
  "id": "coord-break",
  "title": "Request a break",
  "text": "Hi {recipient}, {name} on {callsign}. I need about {minutes} minutes from {time}Z - can you cover?",
  "fields": [
    { "key": "name",    "label": "Your name", "source": "names" },
    { "key": "minutes", "label": "Minutes",   "options": ["5", "10", "15", "20"], "defaultValue": "10" }
  ]
}
```

| Key | Notes |
| --- | --- |
| `key` | The placeholder this describes, without braces. |
| `label` | Text shown beside the input. Defaults to `key`. |
| `source` | `"names"` populates the dropdown from `names.json`. |
| `options` | Inline dropdown choices. Combines with `source`. |
| `allowFreeText` | `false` locks the input to the list. Default `true`. |
| `defaultValue` | Pre-filled value. |

Declaring a field for `{callsign}`, `{recipient}`, `{time}` or `{date}` overrides
the automatic value with an input box.

### `names.json`

```json
{
  "version": 1,
  "names": ["Levi", "Alex", "Sam"]
}
```

Add your name here so it is in the dropdown on every machine you control from.
If you would rather not publish it, put it in `local-names.json` instead — see
below.

## Local files

The plugin keeps its own files in `Documents\vatSys Files\CannedMessages\`
(the **Open folder** button goes straight there):

| File | Purpose |
| --- | --- |
| `config.json` | Settings, written on first run. |
| `cache\messages.json`, `cache\names.json` | Last successful pull from this repository. Used when GitHub is unreachable. |
| `local-messages.json` | Your private templates. Same format as `messages.json`. |
| `local-names.json` | Your private names. Same format as `names.json`. |

Layers stack lowest to highest: templates shipped with the DLL, then the
repository cache, then your local files. Same `id` replaces; new `id` adds. A
category name that already exists is merged rather than duplicated.

### `config.json`

```json
{
  "rawBaseUrl": "https://raw.githubusercontent.com/RealLeviticus/vatSysCannedMessages/main/templates/",
  "refreshOnStartup": true,
  "timeoutSeconds": 10,
  "defaultName": "",
  "maxMessageLength": 200
}
```

Point `rawBaseUrl` at your own fork or a branch to test templates before opening
a pull request. Set `defaultName` to skip picking your name every time.

## Building

Needs Visual Studio 2022 (or Build Tools for Visual Studio 2022) with the .NET
Framework 4.7.2 targeting pack, and vatSys installed.

```powershell
.\build.ps1
.\install.ps1 -Profile Australia
```

`build.ps1` takes `-VatSysPath` if vatSys is not in
`C:\Program Files (x86)\vatSys\bin`, and `install.ps1` takes `-Profile All`.
Close vatSys before installing — it holds the DLL open.

## Matching the vatSys look

The window is a `BaseForm` and takes its palette from `Colours.GetColour`, so it
follows whatever the profile defines:

| Element | Identity |
| --- | --- |
| Window and input backgrounds | `WindowBackground` |
| Labels | `GenericText` |
| Text boxes, combos, tree | `InteractiveText` (matches `vatsys.TextField`) |
| Buttons | left to `GenericButton`'s own defaults |

Two things worth knowing if you touch the styling:

- **Never set `BackColor` on a `GenericButton`.** It paints itself in `OnPaint`,
  filling with `BackColor` and drawing text in `ForeColor`, and its constructor
  already applies the right defaults. In the Australia profile
  `WindowButtonSelected` and `InteractiveText` are both `rgb(0,0,96)`, so
  assigning the former turns the button into a solid blue block with invisible
  text. `WindowButtonSelected` and `WindowButtonDepressed` are hover and press
  states that `GenericButton` applies itself.
- **Show the window with `ShowWithPlacement(owner)`.** vatSys never calls
  `Show()` without an owner. An unowned window is not kept above the maximised
  main form and drops behind it the moment focus returns. `ShowWithPlacement`
  also restores the saved position, keyed on `Control.Name` — so the form needs
  a unique `Name` or it shares its position with every other unnamed window.

## Two vatSys quirks worked around

**The Messages menu category is broken.** `MainForm.LoadPluginMenuItem` handles
`CustomToolStripMenuItemCategory.Messages` by looking the anchor separator
`toolStripSeparatorMessagesFinal` up in `setupToolStripMenuItem.DropDownItems` —
the *Settings* menu — instead of `messagesToolStripMenuItem.DropDownItems`.
`IndexOfKey` returns -1 and the method returns without adding anything, so the
item silently never appears. The `Info` category has the same copy-paste bug.
`Windows`, `Maps` and `Tools` are plain `Add`/`Insert` calls and work.

So the plugin inserts into the Messages menu itself and only falls back to
`AddCustomMenuItem` with the `Windows` category if that fails.

**Plugins are discovered with MEF.** `Plugins.iplugins` is an `[ImportMany]
IEnumerable<IPlugin>` composed via `ComposeParts`, so implementing `IPlugin` is
not enough — the class also needs `[Export(typeof(IPlugin))]`. Without it the
plugin loads but is never instantiated.

## How it talks to the network

vatSys exposes `Network.SendRadioMessage` publicly but keeps the private-message
path (`Network.Instance.SendTextMessage`) internal, so the plugin reaches it by
reflection. It is resolved once at startup and checked before every send; if a
future vatSys build moves it, the window says so and falls back to putting the
message on the clipboard rather than failing silently.
