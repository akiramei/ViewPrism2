prediction:
  request: REQUEST-R1
  change_units:
    - unit: M-DB-007
      confidence: likely
      rationale: "bom/32-mbom.yaml M-DB-007 repositories はコレクション切替で使う normal・image_tags・status件数・統合裁定事象件数の各 read model を所有する。bom/30-ebom.yaml E-UI-REPAIR-039 ECO-098 には、今回と同じ normal 6件／hidden missing 262,045件で全status取得が約1.1秒停止した実績があり、DB境界限定が性能規律として明記されている。表示対象を変えず是正するなら、クエリまたは索引の変更が最有力"
  investigate_units:
    - unit: M-UI-IMAGETAB-035
      confidence: certain
      rationale: "bom/32-mbom.yaml M-UI-IMAGETAB-035 startup がcollection切替、content generation、選択collectionのnormal・image_tags・deleted count取得を所有するため、切替中に呼ばれるread modelと待ち合わせ順を必ず確認する必要がある"
    - unit: M-UI-INTEGRITY-055
      confidence: likely
      rationale: "bom/32-mbom.yaml M-UI-INTEGRITY-055 entry は画像タブ上の事象合計件数を所有し、対象母集合はpending∪missing。missing 262,045件のコレクションだけ遅い症状と一致し得るため、CountIntegrityReviewEventsAsyncが切替完了のクリティカルパスに入っているか調査が必要"
    - unit: M-UI-013
      confidence: uncertain
      rationale: "bom/32-mbom.yaml M-UI-013 startup はshellからImageTabの初期化・取消を接続する。通常のcollection切替所有者はM-UI-IMAGETAB-035だが、shell側の同期的な待ち合わせ有無はBOMだけでは除外できない"
  not_affected:
    - unit: M-SIMSEARCH-021
      rationale: "REQUEST-R1の「類似」はコレクション名であり、類似検索実行の報告ではない。bom/32-mbom.yaml M-SIMSEARCH-021はFindSimilar系検索と特徴量・類似度cacheを所有し、通常のcollection切替は所有しない"
    - unit: M-UI-ORGANIZE-034
      rationale: "bom/32-mbom.yaml M-UI-ORGANIZE-034は整理状態と類似・条件検索、マージ実行を所有するが、要求は検索操作ではなく画像タブのcollection切替である"
    - unit: M-UI-TRASH-032
      rationale: "同じ大量missing事例に近接するが、bom/32-mbom.yaml M-UI-TRASH-032 ECO-098はゴミ箱popupを開く際のGetDeletedByFolderAsync専用契約であり、REQUEST-R1にはゴミ箱を開いた記述がない"
  ebom_items:
    - E-DB-010
    - E-UI-SHELL-021
    - E-UI-BROWSE-022
    - E-UI-INTEGRITY-050
  contracts_to_check:
    - CP-STARTUP-028
    - CP-DB-006
    - CP-INTEGRITY-036
    - CP-UI-G1
  unresolved:
    - question: "コレクション切替時間の大半を占めるのは、normal画像取得、image_tags取得、deleted件数、統合裁定事象件数のどれか"
      needs: "各M-DB-007 read modelの呼出回数・引数と個別所要時間"
    - question: "是正がDBクエリ／複合索引だけで閉じるか、事象件数などを切替完了から分離するsurface変更も必要か"
      needs: "M-UI-IMAGETAB-035の切替時await順序とM-UI-INTEGRITY-055件数取得の依存関係"
    - question: "相対性能要求をどの入力規模・許容差で合格とするか"
      needs: "REQUEST-R1には実測値がなく、CP-STARTUP-028も固定ms閾値を置かないため、normal少数＋missing 262,045件と比較2コレクションを用いた受入基準の追加裁定"