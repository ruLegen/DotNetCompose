; Unshipped analyzer release

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|------
DNC012 | ComposeGenerator | Error | Async composable methods are unsupported
DNC013 | ComposeGenerator | Error | Iterator composable methods are unsupported
DNC014 | ComposeGenerator | Error | By-reference composable parameters are unsupported
DNC015 | ComposeGenerator | Error | Direct composable calls outside composition are unsupported
DNC016 | ComposeGenerator | Error | Read-only composables can only call other read-only composables
DNC017 | ComposeGenerator | Error | Read-only composable delegate arguments must have a verifiable contract
DNC018 | ComposeGenerator | Error | Generated instance composable signatures must not conflict
DNC020 | ComposeGenerator | Error | Composable modes must be valid for their declaration target
DNC021 | ComposeGenerator | Error | Inline composable parameters cannot escape their in-place context
DNC022 | ComposeGenerator | Error | Inline composable methods must be concrete and non-virtual
DNC023 | ComposeGenerator | Error | Composable overrides must preserve method and parameter modes
