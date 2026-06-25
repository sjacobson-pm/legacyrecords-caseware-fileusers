// **********************************************************************
// * Prettier configuration
// ` https://prettier.io/docs/en/options.html
// **********************************************************************

module.exports = {
  // * have prettier wrap at 120 chars
  printWidth: 120,

  // * set tab width to 2 spaces
  tabWidth: 2,

  // * use spaces
  useTabs: false,

  // * add semicolons
  semi: true,

  // * use single quotes
  singleQuote: true,

  // * only add quotes around object properties where required
  quoteProps: 'as-needed',

  // * add trailing commas
  // * trailing commas where valid in ES5 (objects, arrays, etc.). Trailing commas in type parameters in TypeScript
  trailingComma: 'es5',

  // * print spaces between brackets in object literals
  bracketSpacing: true,

  // * put the > of a multi-line HTML (HTML, JSX, Vue, Angular) element at the end of the last line instead of being alone on the next line (does not apply to self closing elements).
  bracketSameLine: true,

  // * always include arrow parens
  arrowParens: 'always',

  // * preserve prose wrapping
  proseWrap: 'preserve',

  // * whitespace is considered insensitive
  htmlWhitespaceSensitivity: 'ignore',

  // * use crlf for end-of-line
  endOfLine: 'crlf',

  // * use double-quotes in JSX
  jsxSingleQuote: false,
};
