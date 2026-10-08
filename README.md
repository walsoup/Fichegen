# fichegen

lesson prep for teachers, written by an llm so you don't have to.

native macos app. windows build on its own branch, under the PROFstudio name. the original python version is still available separately.

## a look inside

![PROFstudio on Windows: lesson form, document preview and AI assistant](https://raw.githubusercontent.com/walsoup/Fichegen/windows-native/screens/fiche-generation.png)

*the windows app, from the screenshots already in this repo. the macos interface is different.*

## what it does

- drafts lesson plans, evaluations and quizzes from a class level, subject and topic.
- uses text from pdf guides and textbooks as context.
- lets you revise generated material through an ai chat panel.
- gives you document styles, a style builder and custom structure templates.
- keeps a local generation history.

on macos, the result panel can export pdf, html and rtf. features and export formats differ between versions.

the output is ai-generated. check facts, instructions, answers and curriculum fit before using it with students.

## get it

| version | download | source |
| --- | --- | --- |
| macos | [version 3.6, dmg](https://github.com/walsoup/Fichegen/releases/tag/v3.6) | [`macos` branch](https://github.com/walsoup/Fichegen/blob/macos/README.md) |
| windows / PROFstudio | [version 1.2.0, installer and portable builds](https://github.com/walsoup/Fichegen/releases/tag/v1.2.0-windows) | [`windows-native` branch](https://github.com/walsoup/Fichegen/blob/windows-native/README.md) |
| python / PyQt6 | run from source | [`python` branch](https://github.com/walsoup/Fichegen/blob/python/README.md) |

macos requires macos 14 or later. the windows version targets windows 10 and 11.

## using the macos app

1. open preferences with `cmd + ,` and configure an ai provider. gemini is supported; other provider and routing settings are available too.
2. choose a class level, subject and lesson topic. add a source pdf or extra instructions if needed.
3. generate a fiche, evaluation or quiz.
4. review the result, edit it directly or through the chat panel, then export.

ai generation needs a connection to the configured provider. prompts and any document content included in a request go to that provider, whose terms and data practices apply. don't send student personal data unless you have permission to do so.

on macos, api keys are stored in the keychain. other preferences are saved locally in the app's application support directory. generation history is also stored locally.

## building the macos app

use xcode on a mac with the macos 14 sdk or later. the repo includes an xcode project and an xcodegen configuration.

```sh
git clone --branch macos https://github.com/walsoup/Fichegen.git
cd Fichegen
open FicheGen.xcodeproj
```

select the `FicheGen` scheme, set your own signing team if xcode asks for one, then build and run.

if you need to regenerate the project, install xcodegen and run:

```sh
xcodegen generate
```

the native macos target uses apple frameworks without third-party swift package dependencies. it bundles `marked.min.js` for markdown rendering.

## license

the macos and windows branches use PolyForm Noncommercial 1.0.0, with the additional terms in each branch's `LICENSE`:

- noncommercial use, modification and sharing are allowed.
- schools and administrators may distribute it internally to teachers, staff and students for teaching and school work, free of charge.
- selling it, bundling it into a paid product or service, or other commercial use needs written permission.
- it is provided "as is", without warranty. generated material needs a qualified person's review.

the python / PyQt6 branch has its own GPL-3.0-only license. the noncommercial terms above do not apply to that branch. GPL allows commercial redistribution too, subject to its conditions.

read the `LICENSE` file for the branch you use. this summary does not replace it.
