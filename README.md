# FunkyORM

FunkyORM is designed to be a low-impedance interface to real SQL. It is a micro-ORM designed to fill the gaps between heavy solutions like EntityFramework and other micro-ORMs like Dapper, without needing to use any specialized querying syntax (like passing field name & value pairs with literal operators).

FunkyORM can be as configuration-free as desired. Most of its behaviors can be achieved using bare entities with few or no annotations, no contexts, and no database or entity models. Simple annotations are supported where needs diverge from the most common use cases. Just write or generate entities for your tables and start querying!

Design philosophy:
- **Be fast**: Competitive benchmarks when compared to similar alternatives
- **Minimize footprint**: completely agnostic to DbContexts, models, joins, and relationships
- **Maximize ease-of-use**: Support lambda queries out of the box
- **Usability over power**:
  - Defines sensible defaults like common PK conventions (e.g., `id`, `tablename_id`, `TableNameId`, etc.)
  - Supports `[key]` attribute for cases where tables diverge from these conventions
  - Auto maps matching column names ignoring case and underscores by default 
  - Ignores properties/columns not present in both source table/view and target entity by default
- **Easily customized**: Supports `System.ComponentModel.DataAnnotations` attributes like `[Table]`, `[Column]`, `[Key]`, `[NotMapped]`

Our goal is to make it easy for developers to get up and running quickly doing what they do 80% of the time, while making it hard for them to shoot themselves in the foot; we avoid automating complex behaviors that should really be thought through more thoroughly, like joins, inclusions, recursions, etc. 

# Quickstart



