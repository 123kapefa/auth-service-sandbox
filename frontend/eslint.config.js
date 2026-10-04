import js from "@eslint/js";
import tanstackQuery from "@tanstack/eslint-plugin-query";
import boundaries from "eslint-plugin-boundaries";
import reactHooks from "eslint-plugin-react-hooks";
import reactRefresh from "eslint-plugin-react-refresh";
import { defineConfig, globalIgnores } from "eslint/config";
import globals from "globals";
import tseslint from "typescript-eslint";

// Слои Feature-Sliced Design сверху вниз: слой импортирует себя и слои ниже. Фичи друг друга не импортируют —
// общее поднимается в entities или shared (как в эталонном проекте платформы).
const fsdLayers = [
  { type: "app", pattern: "src/app/**" },
  { type: "pages", pattern: "src/pages/**" },
  { type: "widgets", pattern: "src/widgets/**" },
  { type: "features", pattern: "src/features/*", mode: "folder", capture: ["slice"] },
  { type: "entities", pattern: "src/entities/**" },
  { type: "shared", pattern: "src/shared/**" },
];

const sameSlice = (type) => ({ type, captured: { slice: "{{from.captured.slice}}" } });

const fsdRules = [
  {
    from: { type: "app" },
    allow: { to: { type: ["pages", "widgets", "features", "entities", "shared"] } },
  },
  {
    from: { type: "pages" },
    allow: { to: { type: ["widgets", "features", "entities", "shared"] } },
  },
  {
    from: { type: "widgets" },
    allow: { to: { type: ["widgets", "features", "entities", "shared"] } },
  },
  {
    from: { type: "features" },
    allow: { to: [sameSlice("features"), { type: "entities" }, { type: "shared" }] },
  },
  { from: { type: "entities" }, allow: { to: { type: ["entities", "shared"] } } },
  { from: { type: "shared" }, allow: { to: { type: ["shared"] } } },
];

export default defineConfig([
  globalIgnores(["dist", "coverage"]),
  {
    files: ["**/*.{ts,tsx}"],
    extends: [
      js.configs.recommended,
      tseslint.configs.strict,
      reactHooks.configs.flat["recommended-latest"],
      reactRefresh.configs.vite,
      tanstackQuery.configs["flat/recommended"],
    ],
    languageOptions: { ecmaVersion: 2022, globals: globals.browser },
    plugins: { boundaries },
    settings: {
      "boundaries/elements": fsdLayers,
      "boundaries/include": ["src/**/*"],
      // Без резолвера алиас @/ не распознаётся, и проверка слоёв молча проходит.
      "import/resolver": { typescript: { alwaysTryTypes: true, project: "./tsconfig.app.json" } },
    },
    rules: {
      "boundaries/dependencies": ["error", { default: "disallow", rules: fsdRules }],
      "@typescript-eslint/consistent-type-imports": [
        "error",
        { prefer: "type-imports", fixStyle: "inline-type-imports" },
      ],
      "@typescript-eslint/no-unused-vars": [
        "error",
        {
          argsIgnorePattern: "^_",
          varsIgnorePattern: "^_",
          caughtErrorsIgnorePattern: "^_",
          ignoreRestSiblings: true,
        },
      ],
      "no-console": ["error", { allow: ["warn", "error"] }],
    },
  },
]);
