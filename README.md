# FunkyORM

FunkyORM is a micro-ORM designed to fill the gaps between heavy solutions like EntityFramework and other micro-ORMs like Dapper, without needing to use a clunky alternate querying syntax like passing name/value pairs.

Design philosophy:
- Minimize footprint: completely agnostic to DbContexts, models, joins, and relationships
- Ease-of-use: Support querying using lambdas by default
- Usable out-of-the-box: 
  - Sensible defaults like identifying common PK conventions 
  - auto map column names ignoring case and underscores where possible
  - only map columns present in both source table/view and target entity
- Easily customized: Support [Column], [Key], [NotMapped]