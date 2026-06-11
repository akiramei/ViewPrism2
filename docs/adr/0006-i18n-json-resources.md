# ADR-0006: i18n は JSON リソース + 自前 LocalizationService(動的切替)を採用する

- 状態: 承認済み(2026-06-11)
- 決定者: 設計AI(委任範囲)

## 文脈
REQ-050(ja/en、欠落キーは ja → キー文字列へフォールバック、例外なし)、REQ-051(再起動不要の即時切替)。原典には next-intl 用の翻訳資産 messages/ja.json(1038 行)・en.json(699 キー)があり、流用したい。

## 決定
- リソース形式: **JSON**(`Assets/i18n/ja.json`, `en.json`)。キーは原典のネームスペースをドット連結でフラット化(例 `tag.createDialog.title`)。プレースホルダ `{name}` 構文を維持
- 解決器: 自前 **LocalizationService**(OC-8 観測契約)。`CultureChanged` 通知で全バインディングを更新(Avalonia の動的リソース/バインディング経由)
- 変換: 原典 ja.json/en.json から V1 該当ネームスペースを機械変換して取り込む(K-I18N に変換規則)

## 却下した代替案
- **ResX**: 動的切替の実装が煩雑、原典 JSON 資産の流用に変換コストが二重にかかる、ソース管理上のレビュー性も劣る
- **Avalonia 向け i18n ライブラリ**: 小規模要件に対し依存追加の価値が薄く、フォールバック規則(REQ-050)を正確に制御しづらい

## 影響
- フォールバック・補間ロジックは core として unit 受入(CP-I18N-010)
- 訳文の品質・自然さは golden(G-5)+承認者 maintainer
