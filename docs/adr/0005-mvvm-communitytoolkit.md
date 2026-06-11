# ADR-0005: MVVM 基盤に CommunityToolkit.Mvvm を採用する

- 状態: 承認済み(2026-06-11)
- 決定者: 設計AI(委任範囲)

## 文脈
原典の React Context + hooks による状態管理を、Avalonia の MVVM(ViewModel + データバインディング)へ置き換える。ViewModel は核ロジック(選択・ナビゲーション)を含み unit 受入対象(20-spec.md §2.6)。

## 決定
**CommunityToolkit.Mvvm 8.4.2** を採用する(`[ObservableProperty]`、`[RelayCommand]`、`ObservableObject`)。

## 根拠
- ソースジェネレータによる定型コード削減 = 工場間ばらつき(独自 INPC 実装の発明)の抑制
- Microsoft 公式メンテナンス、MIT、Avalonia との組み合わせ実績が豊富

## 却下した代替案
- **ReactiveUI**: 強力だが Rx の学習コストと流儀の自由度が高く、BomDD 的には出力のばらつき要因
- **素の INotifyPropertyChanged**: 定型コードがずる・ミスの温床

## 影響
- ViewModel の作法(プロパティ・コマンド・非同期コマンド)は K-BOM(K-MVVM)に定型を記載し、工場の判断余地を消す
