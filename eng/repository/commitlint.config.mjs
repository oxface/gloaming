export default {
  extends: ["@commitlint/config-conventional"],
  rules: {
    // Capitalization does not change the meaning of a conventional commit.
    "subject-case": [0],
  },
};
